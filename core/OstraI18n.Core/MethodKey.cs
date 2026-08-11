namespace OstraI18n.Core
{
    /// Идентификатор метода, одинаковый при построении из Mono.Cecil (офлайн)
    /// и из System.Reflection (рантайм). Cecil записывает вложенные типы через '/',
    /// Reflection — через '+'; без приведения к одной форме каталог не находит цель.
    public static class MethodKey
    {
        public static string Normalize(string typeFullName)
        {
            if (string.IsNullOrEmpty(typeFullName)) return typeFullName;
            return typeFullName.Replace('/', '+');
        }

        public static string Make(string typeFullName, string methodName, int paramCount)
        {
            return Normalize(typeFullName) + "::" + methodName + "/" + paramCount;
        }
    }
}
