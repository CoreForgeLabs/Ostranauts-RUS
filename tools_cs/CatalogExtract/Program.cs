using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OstraI18n.Core;

static class Program
{
    // Методы, присваивание в которые означает вывод текста на экран.
    static readonly string[] TextSinks =
    {
        "TMPro.TMP_Text::set_text",
        "UnityEngine.UI.Text::set_text",
        "TMPro.TMP_Text::SetText",
    };

    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("использование: CatalogExtract <Assembly-CSharp.dll> <выход.json>");
            return 2;
        }
        var asmPath = args[0];
        var outPath = args[1];

        if (!File.Exists(asmPath)) { Console.Error.WriteLine("нет файла: " + asmPath); return 2; }

        var asm = AssemblyDefinition.ReadAssembly(asmPath);
        var entries = new List<Dictionary<string, object>>();
        int methodsWithSink = 0;

        foreach (var type in asm.MainModule.GetTypes())
        {
            foreach (var m in type.Methods)
            {
                if (!m.HasBody) continue;

                bool hasSink = m.Body.Instructions.Any(i =>
                    (i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Call)
                    && i.Operand is MethodReference mr
                    && TextSinks.Any(s => (mr.DeclaringType.FullName + "::" + mr.Name) == s));

                if (!hasSink) continue;
                methodsWithSink++;

                var key = MethodKey.Make(type.FullName, m.Name, m.Parameters.Count);
                var seen = new Dictionary<string, int>();

                foreach (var instr in m.Body.Instructions)
                {
                    if (instr.OpCode != OpCodes.Ldstr) continue;
                    var lit = instr.Operand as string;
                    if (string.IsNullOrEmpty(lit)) continue;

                    seen.TryGetValue(lit, out int ord);
                    seen[lit] = ord + 1;

                    entries.Add(new Dictionary<string, object>
                    {
                        ["methodKey"] = key,
                        ["literal"] = lit,
                        ["ordinal"] = ord,
                        ["candidate"] = LooksLikeUiText(lit),
                    });
                }
            }
        }

        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, JsonSerializer.Serialize(entries, opts));

        int cand = entries.Count(e => (bool)e["candidate"]);
        Console.WriteLine($"методов с выводом текста: {methodsWithSink}");
        Console.WriteLine($"литералов всего: {entries.Count}");
        Console.WriteLine($"кандидатов в UI-текст: {cand}");
        Console.WriteLine("записано: " + outPath);
        return 0;
    }

    // Отсекает идентификаторы, имена ассетов и коды — они не показываются игроку.
    static bool LooksLikeUiText(string s)
    {
        if (s.Length < 2 || s.Length > 200) return false;
        if (!s.Any(char.IsLetter)) return false;
        if (s.Any(c => c == '/' || c == '\\')) return false;      // пути к ассетам и файлам
        if (s.StartsWith("<")) return false;                       // куски разметки TMP
        // ИдентификаторыБезПробелов вида ItmFloorGrate01 / TIsToolWelding:
        // смешанный регистр, есть цифры, нет пробелов — игроку такое не показывают.
        if (!s.Contains(' ')
            && s.Length > 3
            && s.Any(char.IsUpper) && s.Any(char.IsLower)
            && s.Any(char.IsDigit)) return false;
        return true;
    }
}
