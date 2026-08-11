using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
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
        if (args.Length >= 1 && args[0] == "--probe-overloads")
        {
            return ProbeOverloads(args[1]);
        }
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

                // short-form stloc.0/ldloc.0 и т.п. не несут явного индекса переменной в Operand —
                // SimplifyMacros разворачивает их в длинную форму с VariableDefinition, что нужно
                // для отслеживания "литерал сохранён в переменную, переменная передана в sink".
                m.Body.SimplifyMacros();

                var key = MethodKey.Make(type.FullName, m.Name, m.Parameters.Count);
                var seen = new Dictionary<string, int>();

                foreach (var instr in m.Body.Instructions)
                {
                    if (instr.OpCode != OpCodes.Ldstr) continue;
                    var lit = instr.Operand as string;
                    if (string.IsNullOrEmpty(lit)) continue;

                    seen.TryGetValue(lit, out int ord);
                    seen[lit] = ord + 1;

                    // Метод содержит вызов text-sink где-то в теле — но это не значит, что
                    // КАЖДЫЙ литерал в нём является текстом. Литералы также используются как
                    // условия (HasCond("IsMale")), ключи словарей, имена анимаций. Подмена
                    // такого литерала не портит текст — она портит логику метода (см. падение
                    // NullReferenceException в GUIChargenCareer.PageResume после того, как
                    // подошедший под этот метод литерал вне текстового контекста был утверждён).
                    // Безопасное сужение: литерал берётся в кандидаты, только если он реально
                    // достигает sink — либо напрямую (ldstr; callvirt set_text), либо через
                    // одну промежуточную переменную (ldstr; stloc N; ...; ldloc N; callvirt).
                    // Не ловит SetText(text, доп.аргументы) и конкатенацию — вне объёма Фазы 1,
                    // недобор здесь безопаснее перебора.
                    bool reachesSink = IsUsedAsSinkArgument(instr, TextSinks);

                    entries.Add(new Dictionary<string, object>
                    {
                        ["methodKey"] = key,
                        ["literal"] = lit,
                        ["ordinal"] = ord,
                        ["candidate"] = reachesSink && LooksLikeUiText(lit),
                        ["key"] = MakeKey(type.FullName, lit),
                        ["approved"] = false,
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

    static bool IsSinkCall(Instruction i, string[] sinks) =>
        (i.OpCode == OpCodes.Callvirt || i.OpCode == OpCodes.Call)
        && i.Operand is MethodReference mr
        && sinks.Any(s => (mr.DeclaringType.FullName + "::" + mr.Name) == s);

    // True, если значение, положенное на стек данным ldstr, доходит до вызова text-sink —
    // напрямую следующей инструкцией, либо через одну промежуточную локальную переменную
    // (типичный паттерн `string s = "X"; label.text = s;`). Окно поиска использования
    // переменной ограничено 40 инструкциями вперёд — достаточно для одного метода-страницы
    // UI, не превращается в полный анализ потока данных по всей сборке.
    static bool IsUsedAsSinkArgument(Instruction ldstr, string[] sinks)
    {
        var next = ldstr.Next;
        if (next == null) return false;
        if (IsSinkCall(next, sinks)) return true;

        if (next.OpCode == OpCodes.Stloc && next.Operand is VariableDefinition vdef)
        {
            var scan = next.Next;
            for (int i = 0; scan != null && i < 40; i++, scan = scan.Next)
            {
                if (scan.OpCode == OpCodes.Ldloc && scan.Operand is VariableDefinition vd2 && vd2 == vdef)
                    return scan.Next != null && IsSinkCall(scan.Next, sinks);
                // переменная переприсвоена раньше, чем использована как текст — не наш случай
                if (scan.OpCode == OpCodes.Stloc && scan.Operand is VariableDefinition vd3 && vd3 == vdef)
                    return false;
            }
        }
        return false;
    }

    // Диагностика: сколько (тип, имя метода, число параметров) соответствуют
    // более чем одному реальному методу — методы-перегрузки, неразличимые MethodKey.
    static int ProbeOverloads(string asmPath)
    {
        var asm = AssemblyDefinition.ReadAssembly(asmPath);
        var groups = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var type in asm.MainModule.GetTypes())
        {
            var byNamePc = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var m in type.Methods)
            {
                var k = m.Name + "/" + m.Parameters.Count;
                byNamePc.TryGetValue(k, out int c);
                byNamePc[k] = c + 1;
            }
            foreach (var kv in byNamePc)
                if (kv.Value > 1)
                    groups[MethodKey.Make(type.FullName, kv.Key.Substring(0, kv.Key.LastIndexOf('/')),
                        int.Parse(kv.Key.Substring(kv.Key.LastIndexOf('/') + 1)))] = kv.Value;
        }
        Console.WriteLine("неоднозначных methodKey (name+paramCount, но разные типы параметров): " + groups.Count);
        foreach (var kv in groups) Console.WriteLine("  " + kv.Key + " x" + kv.Value);
        return 0;
    }

    // Ключ строится из имени типа и текста литерала, чтобы быть читаемым
    // и совпадать по стилю с существующими ключами игры (GUI_QUIT_CONFIRM).
    static string MakeKey(string typeName, string literal)
    {
        var screen = typeName;
        int dot = screen.LastIndexOf('.');
        if (dot >= 0) screen = screen.Substring(dot + 1);
        screen = screen.Replace("GUI", "").Replace("Panel", "").Replace("Popup", "");
        screen = Slug(screen);

        var body = Slug(literal);
        if (body.Length > 40) body = body.Substring(0, 40).TrimEnd('_');
        if (body.Length == 0) body = "TEXT";

        return "GUI_" + (screen.Length > 0 ? screen + "_" : "") + body;
    }

    static string Slug(string s)
    {
        var sb = new System.Text.StringBuilder();
        bool lastUnderscore = false;
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(char.ToUpperInvariant(ch));
                lastUnderscore = false;
            }
            else if (!lastUnderscore && sb.Length > 0)
            {
                sb.Append('_');
                lastUnderscore = true;
            }
        }
        return sb.ToString().Trim('_');
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
