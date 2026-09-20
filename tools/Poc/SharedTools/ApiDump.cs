using System.Reflection;

namespace DuetDiagram.Poc.SharedTools;

/// <summary>
/// 把候选库的公开接口原样打印出来。
/// </summary>
/// <remarks>
/// 评估一个第三方库能不能满足需求时，说明文档只说明作者想强调的部分，
/// 缺什么往往不会写。直接读元数据里的公开成员可以立刻看出"有没有这个东西"，
/// 也能看出构造参数带不带默认值、返回类型是否可组合。这一步比翻文档可靠。
/// </remarks>
internal static class ApiDump
{
    public static void Run(params string[] assemblyNames)
    {
        foreach (var name in assemblyNames)
        {
            Console.WriteLine($"========== {name} ==========");

            Assembly assembly;

            try
            {
                assembly = Assembly.Load(name);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  <load failed: {ex.Message}>");
                continue;
            }

            Console.WriteLine($"  version: {assembly.GetName().Version}");

            foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
            {
                Console.WriteLine();
                Console.WriteLine($"  {(type.IsEnum ? "enum" : type.IsValueType ? "struct" : type.IsInterface ? "interface" : "class")} {type.FullName}");

                if (type.IsEnum)
                {
                    Console.WriteLine($"      values: {string.Join(", ", Enum.GetNames(type))}");
                    continue;
                }

                // 只看当前类型自己声明的成员，不带上继承来的，
                // 否则记录类型派生出来的复制与比较方法会把输出淹没。
                foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    Console.WriteLine($"      prop {Describe(property.PropertyType)} {property.Name}");
                }

                foreach (var constructor in type.GetConstructors())
                {
                    var parameters = string.Join(", ", constructor.GetParameters().Select(p => $"{Describe(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? " = …" : string.Empty)}"));
                    Console.WriteLine($"      ctor ({parameters})");
                }

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                             .Where(m => !m.IsSpecialName))
                {
                    var parameters = string.Join(", ", method.GetParameters().Select(p => $"{Describe(p.ParameterType)} {p.Name}{(p.HasDefaultValue ? " = …" : string.Empty)}"));
                    Console.WriteLine($"      {(method.IsStatic ? "static " : string.Empty)}{Describe(method.ReturnType)} {method.Name}({parameters})");
                }
            }

            Console.WriteLine();
        }
    }

    private static string Describe(Type type)
    {
        if (type.IsGenericType)
        {
            // 泛型参数本身也是"泛型类型"，但它的名字里没有反引号（就是一个 T）。
            // 不判一下就会在切片时越界。
            var marker = type.Name.IndexOf('`');
            var name = marker < 0 ? type.Name : type.Name[..marker];

            return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Describe))}>";
        }

        return type.Name;
    }
}
