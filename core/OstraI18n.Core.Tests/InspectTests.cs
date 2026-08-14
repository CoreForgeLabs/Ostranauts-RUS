using System;
using System.Reflection;
using System.IO;
using System.Text;
using Xunit;

namespace OstraI18n.Core.Tests
{
    public class InspectTests
    {
        [Fact]
        public void FindMissingItem()
        {
            var managed = @"F:\Games\Steam\steamapps\common\Ostranauts\Ostranauts_Data\Managed";
            var asm = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));

            var sb = new StringBuilder();
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
                                if (s.Contains("Missing item") || s.Contains("Невозможно выполнить") || s.Contains("собирается"))
                                {
                                    sb.AppendLine($"[FOUND] {t.FullName}.{m.Name} -> '{s.Replace("\n", "\\n")}'");
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            Assert.True(false, sb.ToString());
        }
    }
}
