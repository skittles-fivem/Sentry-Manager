using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace SentrySix.Core.Tests
{
    /// <summary>
    /// The UI packs' tests (Horizon*Tests, Flux*Tests, Aurora*Tests,
    /// Daylight*Tests), found by name so a new page's tests need no line in
    /// Program.cs: each has a static Register(Action&lt;string, Action&gt;) or
    /// Register(Action&lt;string, Func&lt;Task&gt;&gt;), as the others.
    /// </summary>
    static class PackTests
    {
        static readonly string[] Packs = { "Horizon", "Flux", "Aurora", "Daylight" };

        public static void Register(Action<string, Action> sync, Action<string, Func<Task>> async)
        {
            var types = typeof(PackTests).Assembly.GetTypes()
                .Where(t => t.Namespace == typeof(PackTests).Namespace && t.Name.EndsWith("Tests") && Packs.Any(p => t.Name.StartsWith(p)))
                .OrderBy(t => Array.FindIndex(Packs, p => t.Name.StartsWith(p))).ThenBy(t => t.Name, StringComparer.Ordinal);
            foreach (Type t in types)
            {
                foreach (MethodInfo m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(m => m.Name == "Register"))
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(Action<string, Action>)) m.Invoke(null, new object[] { sync });
                    else if (ps.Length == 1 && ps[0].ParameterType == typeof(Action<string, Func<Task>>)) m.Invoke(null, new object[] { async });
                    else if (ps.Length == 2) m.Invoke(null, new object[] { sync, async });
                }
            }
        }
    }
}
