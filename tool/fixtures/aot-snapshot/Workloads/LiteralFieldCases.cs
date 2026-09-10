using System;
using System.Reflection;

namespace HybridCLR.Lab.ModuleEvolution
{
    public enum LiteralMode : long { Selected = LiteralFieldCases.EnumNumber }
    public static class GenericLiterals<T>
    {
        public const int Number = LiteralFieldCases.Number;
        public const string Text = LiteralFieldCases.Text;
    }
    public static class LiteralFieldCases
    {
#if LITERALS_CURRENT
        public const int Number = -202;
        public const long EnumNumber = 9000000000001L;
        public const ulong Unsigned = ulong.MaxValue;
        public const string Text = "Current 常量\0尾";
        public const string Nullable = "now present";
        public const string BecomesNull = null;
        public const char Character = '新';
        public const bool Boolean = true;
        public const float Single = -1.25f;
        public const double Double = 2.5;
#else
        public const int Number = 101;
        public const long EnumNumber = -101L;
        public const ulong Unsigned = 101UL;
        public const string Text = "Base";
        public const string Nullable = null;
        public const string BecomesNull = "Base";
        public const char Character = '旧';
        public const bool Boolean = false;
        public const float Single = 3.5f;
        public const double Double = -10.25;
#endif
        public const object NullObject = null;
        public const LiteralMode Choice = LiteralMode.Selected;
        private const int PrivateNumber = Number;

        private static int VerifyField(Type owner, string name, object raw, object boxed)
        {
            var field = owner.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null || !field.IsLiteral) throw new InvalidOperationException("Literal field missing: " + owner + "::" + name);
            for (int repeat = 0; repeat != 2; ++repeat)
                if (!object.Equals(field.GetRawConstantValue(), raw) || !object.Equals(field.GetValue(null), boxed))
                    throw new InvalidOperationException("Wrong literal value: " + owner + "::" + name +
                        " raw=" + field.GetRawConstantValue() + " boxed=" + field.GetValue(null));
            return 4;
        }
        public static void Verify()
        {
            int count = 0;
            Type owner = typeof(LiteralFieldCases);
            count += VerifyField(owner, "Number", Number, Number);
            count += VerifyField(owner, "EnumNumber", EnumNumber, EnumNumber);
            count += VerifyField(owner, "Unsigned", Unsigned, Unsigned);
            count += VerifyField(owner, "Text", Text, Text);
            count += VerifyField(owner, "Nullable", Nullable, Nullable);
            count += VerifyField(owner, "BecomesNull", BecomesNull, BecomesNull);
            count += VerifyField(owner, "Character", Character, Character);
            count += VerifyField(owner, "Boolean", Boolean, Boolean);
            count += VerifyField(owner, "Single", Single, Single);
            count += VerifyField(owner, "Double", Double, Double);
            count += VerifyField(owner, "NullObject", null, null);
            count += VerifyField(owner, "Choice", EnumNumber, Choice);
            count += VerifyField(owner, "PrivateNumber", PrivateNumber, PrivateNumber);
            count += VerifyField(typeof(LiteralMode), "Selected", EnumNumber, LiteralMode.Selected);
            foreach (Type generic in new[] { typeof(GenericLiterals<>), typeof(GenericLiterals<int>), typeof(GenericLiterals<string>) })
            {
                // Open generic GetValue is not supported by the CLR; raw
                // metadata is valid and should still follow Current.
                if (generic.ContainsGenericParameters)
                {
                    if (!object.Equals(generic.GetField("Number").GetRawConstantValue(), Number) ||
                        !object.Equals(generic.GetField("Text").GetRawConstantValue(), Text))
                        throw new InvalidOperationException("Wrong open generic literal metadata.");
                    count += 2;
                }
                else
                {
                    count += VerifyField(generic, "Number", Number, Number);
                    count += VerifyField(generic, "Text", Text, Text);
                }
            }
            Console.WriteLine("DHE literal reflection pass: " + Number + ":" + count);
        }
    }
}
