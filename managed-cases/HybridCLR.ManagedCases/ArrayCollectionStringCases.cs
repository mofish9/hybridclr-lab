using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HybridCLR.Lab.ManagedCases
{
    public static partial class CaseRegistry
    {
        private static void RegisterArrayCollectionAndStringCases(List<CaseDefinition> cases)
        {
            RegisterCase(cases, "array_clear_and_fill", "array", ArrayClearAndFill, "1,9,3", features: new[] { "array", "clear", "stelem" });
            RegisterCase(cases, "array_reverse_segment", "array", ArrayReverseSegment, "6,5,4,3,2,1", features: new[] { "array", "reverse", "range" });
            RegisterCase(cases, "array_binary_search", "array", ArrayBinarySearch, "2,-3", features: new[] { "array", "binary-search" });
            RegisterCase(cases, "struct_array_mutation", "array", StructArrayMutation, "3,7,11", features: new[] { "array", "struct", "ldelema" });
            RegisterCase(cases, "rectangular_array_write", "array", RectangularArrayWrite, "10,20,30,60", features: new[] { "array", "multidimensional", "write" });
            RegisterCase(cases, "array_segment_enumeration", "array", ArraySegmentEnumeration, "2,4,6", features: new[] { "array", "enumerator", "struct" });
            RegisterCase(cases, "list_insert_remove", "collection", ListInsertRemove, "1,4,3,3", features: new[] { "list", "remove", "insert" });
            RegisterCase(cases, "dictionary_struct_values", "collection", DictionaryStructValues, "30,2", features: new[] { "dictionary", "struct", "generic" });
            RegisterCase(cases, "dictionary_custom_comparer", "collection", DictionaryCustomComparer, "2,Alpha", features: new[] { "dictionary", "comparer", "string" });
            RegisterCase(cases, "hashset_set_operations", "collection", HashSetSetOperations, "1,2,3,4,5", features: new[] { "hashset", "set" });
            RegisterCase(cases, "queue_stack_roundtrip", "collection", QueueStackRoundtrip, "first,last", "2", features: new[] { "queue", "stack" });
            RegisterCase(cases, "linked_list_navigation", "collection", LinkedListNavigation, "a,b,c", features: new[] { "linked-list", "reference" });
            RegisterCase(cases, "sorted_dictionary_order", "collection", SortedDictionaryOrder, "1:one;2:two;3:three", features: new[] { "sorted-dictionary", "enumeration" });
            RegisterCase(cases, "string_builder_editing", "string", StringBuilderEditing, "hybrid-clr", features: new[] { "stringbuilder", "insert", "remove" });
            RegisterCase(cases, "string_search_comparison", "string", StringSearchComparison, "6,True,False", features: new[] { "string", "ordinal", "search" });
            RegisterCase(cases, "string_char_enumeration", "string", StringCharEnumeration, "ABC,198", features: new[] { "string", "char", "foreach" });
            RegisterCase(cases, "string_split_join", "string", StringSplitJoin, "a|b|c|d", features: new[] { "string", "split", "join" });
            RegisterCase(cases, "tuple_deconstruction", "value-type", TupleDeconstruction, "left:7", features: new[] { "valuetuple", "deconstruction" });
            RegisterCase(cases, "key_value_pair_copy", "collection", KeyValuePairCopy, "alpha=9", features: new[] { "keyvaluepair", "struct" });
            RegisterCase(cases, "custom_sort_comparer", "collection", CustomSortComparer, "1,2,3,4", features: new[] { "sort", "comparer", "delegate" });
        }

        private static CaseObservation ArrayClearAndFill()
        {
            int[] values = { 1, 2, 3 };
            Array.Clear(values, 1, 1);
            values[1] = 9;
            return new CaseObservation(JoinInts(values));
        }

        private static CaseObservation ArrayReverseSegment()
        {
            int[] values = { 1, 2, 3, 4, 5, 6 };
            Array.Reverse(values, 0, values.Length);
            return new CaseObservation(JoinInts(values));
        }

        private static CaseObservation ArrayBinarySearch()
        {
            int[] values = { 1, 3, 5, 7, 9 };
            int found = Array.BinarySearch(values, 5);
            int missing = Array.BinarySearch(values, 4);
            return Observation(found, missing);
        }

        private static CaseObservation StructArrayMutation()
        {
            Cell[] cells = { new Cell(1, 2), new Cell(3, 4), new Cell(5, 6) };
            cells[0].Add(2);
            cells[1].Add(4);
            cells[2].Add(6);
            return new CaseObservation(JoinInts(cells.Select(cell => cell.Value)));
        }

        private static CaseObservation RectangularArrayWrite()
        {
            int[,] values = new int[2, 2];
            values[0, 0] = 10;
            values[0, 1] = 20;
            values[1, 0] = 30;
            values[1, 1] = values[0, 0] + values[0, 1] + values[1, 0];
            return new CaseObservation(FormattableString.Invariant($"{values[0, 0]},{values[0, 1]},{values[1, 0]},{values[1, 1]}"));
        }

        private static CaseObservation ArraySegmentEnumeration()
        {
            int[] values = { 1, 2, 4, 6, 8 };
            ArraySegment<int> segment = new ArraySegment<int>(values, 1, 3);
            return new CaseObservation(JoinInts(segment));
        }

        private static CaseObservation ListInsertRemove()
        {
            List<int> values = new List<int> { 1, 3, 5 };
            values.Insert(1, 4);
            values.Remove(5);
            values.Add(3);
            return new CaseObservation(JoinInts(values));
        }

        private static CaseObservation DictionaryStructValues()
        {
            Dictionary<int, Cell> values = new Dictionary<int, Cell>
            {
                [1] = new Cell(10, 11),
                [2] = new Cell(20, 21),
            };
            int sum = values[1].Value + values[2].Value;
            return Observation(sum, values.Count);
        }

        private static CaseObservation DictionaryCustomComparer()
        {
            Dictionary<string, int> values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["Alpha"] = 2,
            };
            int alpha = values["alpha"];
            return new CaseObservation(FormattableString.Invariant($"{alpha},{values.Keys.Single()}"));
        }

        private static CaseObservation HashSetSetOperations()
        {
            HashSet<int> left = new HashSet<int> { 1, 2, 3, 4 };
            HashSet<int> right = new HashSet<int> { 3, 4, 5 };
            left.UnionWith(right);
            int[] values = new int[left.Count];
            left.CopyTo(values);
            Array.Sort(values);
            return new CaseObservation(JoinInts(values));
        }

        private static CaseObservation QueueStackRoundtrip()
        {
            Queue<string> queue = new Queue<string>();
            queue.Enqueue("first");
            queue.Enqueue("middle");
            queue.Enqueue("last");
            string first = queue.Dequeue();
            Stack<string> stack = new Stack<string>(queue);
            string last = stack.Pop();
            return new CaseObservation(first + "," + last, queue.Count.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation LinkedListNavigation()
        {
            LinkedList<string> values = new LinkedList<string>(new[] { "a", "c" });
            LinkedListNode<string> first = values.First!;
            values.AddAfter(first, "b");
            return new CaseObservation(string.Join(",", values));
        }

        private static CaseObservation SortedDictionaryOrder()
        {
            SortedDictionary<int, string> values = new SortedDictionary<int, string>
            {
                [3] = "three",
                [1] = "one",
                [2] = "two",
            };
            StringBuilder builder = new StringBuilder();
            foreach (KeyValuePair<int, string> pair in values)
            {
                if (builder.Length > 0) builder.Append(';');
                builder.Append(pair.Key).Append(':').Append(pair.Value);
            }

            return new CaseObservation(builder.ToString());
        }

        private static CaseObservation StringBuilderEditing()
        {
            StringBuilder builder = new StringBuilder("hybrid");
            builder.Append("clr").Insert(6, '-');
            builder.Remove(0, 1).Insert(0, 'h');
            return new CaseObservation(builder.ToString());
        }

        private static CaseObservation StringSearchComparison()
        {
            string value = "HybridCLR";
            int index = value.IndexOf("clr", StringComparison.OrdinalIgnoreCase);
            bool equal = string.Equals(value, "hybridclr", StringComparison.OrdinalIgnoreCase);
            bool ordinal = string.Equals(value, "hybridclr", StringComparison.Ordinal);
            return new CaseObservation(FormattableString.Invariant($"{index},{equal},{ordinal}"));
        }

        private static CaseObservation StringCharEnumeration()
        {
            string value = "ABC";
            int sum = 0;
            foreach (char character in value) sum += character;
            return Observation(value, sum);
        }

        private static CaseObservation StringSplitJoin()
        {
            string[] parts = "a,b,c,d".Split(',');
            return new CaseObservation(string.Join("|", parts));
        }

        private static CaseObservation TupleDeconstruction()
        {
            (string label, int value) = ("left", 7);
            return new CaseObservation(FormattableString.Invariant($"{label}:{value}"));
        }

        private static CaseObservation KeyValuePairCopy()
        {
            KeyValuePair<string, int> pair = new KeyValuePair<string, int>("alpha", 9);
            return new CaseObservation(FormattableString.Invariant($"{pair.Key}={pair.Value}"));
        }

        private static CaseObservation CustomSortComparer()
        {
            List<int> values = new List<int> { 4, 1, 3, 2 };
            values.Sort((left, right) => left.CompareTo(right));
            return new CaseObservation(JoinInts(values));
        }

        private struct Cell
        {
            public Cell(int value, int extra)
            {
                Value = value;
                Extra = extra;
            }

            public int Value { get; private set; }

            public int Extra { get; }

            public void Add(int amount)
            {
                Value += amount;
            }
        }
    }
}
