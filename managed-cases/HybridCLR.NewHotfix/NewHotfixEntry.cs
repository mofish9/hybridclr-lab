using System;
using System.Reflection;

namespace HybridCLR.Lab.NewHotfix
{
    public static class NewHotfixEntry
    {
        public static int Execute()
        {
            var value = new GenericBox<int>(37);
            MethodInfo combine = typeof(NewHotfixEntry).GetMethod("Combine",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (combine == null) throw new MissingMethodException(typeof(NewHotfixEntry).FullName,
                "Combine");
            return Convert.ToInt32(combine.Invoke(null, new object[] { value.Value, 5 }));
        }

        private static int Combine(int value, int suffix) => checked(value * 100 + suffix);

        private sealed class GenericBox<T>
        {
            public GenericBox(T value) { Value = value; }

            public T Value { get; }
        }
    }
}
