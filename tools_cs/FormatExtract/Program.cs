using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using OstraI18n.Core;

static class Program
{
    static readonly string[] TextSinks =
    {
        "TMPro.TMP_Text::set_text",
        "UnityEngine.UI.Text::set_text",
        "TMPro.TMP_Text::SetText",
    };

    // Вызовы, чей результат — составная строка (не единичный ldstr).
    static readonly string[] CompositionCalls =
    {
        "System.String::Concat",
        "System.String::Format",
    };

    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("использование: FormatExtract <Assembly-CSharp.dll> <patches/formats.md>");
            return 2;
        }
        var asmPath = args[0];
        var outPath = args[1];
        if (!File.Exists(asmPath)) { Console.Error.WriteLine("нет файла: " + asmPath); return 2; }

        var asm = AssemblyDefinition.ReadAssembly(asmPath);
        var report = new List<string>();
        int sites = 0;

        foreach (var type in asm.MainModule.GetTypes())
        {
            foreach (var m in type.Methods)
            {
                if (!m.HasBody) continue;
                bool hasSink = m.Body.Instructions.Any(i => IsCall(i, TextSinks));
                if (!hasSink) continue;

                m.Body.SimplifyMacros();
                var key = MethodKey.Make(type.FullName, m.Name, m.Parameters.Count);

                foreach (var instr in m.Body.Instructions)
                {
                    if (!IsCall(instr, CompositionCalls)) continue;
                    if (!ReachesSinkWithinWindow(instr, TextSinks, 60)) continue;

                    var fragments = CollectPrecedingLiterals(instr, 30);
                    if (fragments.Count == 0) continue;

                    sites++;
                    report.Add("### " + key + " (composition site " + sites + ")\n" +
                               string.Join("\n", fragments.Select(f => "- `" + Escape(f) + "`")) + "\n");
                }
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        var header = "# Места композиции строк (" + sites + ")\n\n" +
                     "Извлечено автоматически, **не применено**. Каждая запись — вызов " +
                     "`String.Concat`/`String.Format`, чей результат достигает text-sink, " +
                     "и литеральные фрагменты, найденные в предшествующих 30 инструкциях того " +
                     "же метода (может включать фрагменты из несвязанной композиции чуть выше по " +
                     "методу — при вычитке проверять реальную принадлежность конкретному вызову).\n\n";
        File.WriteAllText(outPath, header + string.Join("\n", report));

        Console.WriteLine("мест композиции: " + sites);
        Console.WriteLine("записано: " + outPath);
        return 0;
    }

    static bool IsCall(Instruction i, string[] names) =>
        (i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Call)
        && i.Operand is MethodReference mr
        && names.Any(s => (mr.DeclaringType.FullName + "::" + mr.Name) == s);

    static bool ReachesSinkWithinWindow(Instruction from, string[] sinks, int window)
    {
        var cur = from.Next;
        for (int i = 0; i < window && cur != null; i++, cur = cur.Next)
        {
            if (IsCall(cur, sinks)) return true;
            if (cur.OpCode == OpCodes.Stloc) return true; // сохранено в переменную — за пределами простого окна, но не false negative важнее, чем ложный positive здесь: помечаем как достигающее, ручная проверка отсеет
        }
        return false;
    }

    static List<string> CollectPrecedingLiterals(Instruction compositionCall, int window)
    {
        var result = new List<string>();
        var cur = compositionCall.Previous;
        for (int i = 0; i < window && cur != null; i++, cur = cur.Previous)
        {
            if (cur.OpCode == OpCodes.Ldstr && cur.Operand is string s && !string.IsNullOrEmpty(s))
                result.Insert(0, s);
        }
        return result;
    }

    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("`", "'").Replace("\n", "\\n");
}
