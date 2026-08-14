using System;
using System.Reflection;
using System.IO;

namespace OstraI18n.Core
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var managed = @"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\Managed";
            var asm = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));

            Console.WriteLine("=== SCANNING FOR 'Missing item' AND 'собирается' ===");
            foreach (var t in asm.GetTypes())
            {
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    MethodBody body = null;
                    try { body = m.GetMethodBody(); } catch { }
                    if (body == null) continue;
                    var bytes = body.GetILAsByteArray();
                    if (bytes == null) continue;

                    for (int i = 0; i < bytes.Length - 4; i++)
                    {
                        if (bytes[i] == 0x72)
                        {
                            int token = BitConverter.ToInt32(bytes, i + 1);
                            try
                            {
                                string s = m.Module.ResolveString(token);
                                if (s.Contains("Missing item") || s.Contains("собирается") || s.Contains("Невозможно") || s.Contains("Is Сварочный"))
                                {
                                    Console.WriteLine($"[FOUND] {t.FullName}.{m.Name} -> '{s.Replace("\n", "\\n")}'");
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }
    }
}
