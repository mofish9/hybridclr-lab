using System;
using System.Runtime.CompilerServices;

namespace HybridCLR.Lab.ManagedCasesAot
{
#if DHE_STRUCTURE_CURRENT
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Field)]
    public sealed class DheMetadataMarkerAttribute : Attribute
    {
        public DheMetadataMarkerAttribute(int value, string label)
        {
            Value = value;
            Label = label;
        }

        public int Value { get; }
        public string Label { get; }
    }
#endif

    public static class DheMultiBaseProbe
    {
        public static int CurrentValue()
        {
#if DHE_BASE2
            return 2;
#else
            return 1;
#endif
        }
    }

#if DHE_STRUCTURE_CURRENT
    [DheMetadataMarker(2101, "current-type")]
#endif
    public class DheDemoCalculator
    {
#if DHE_LAYOUT_CHANGE
        public object AddedReferenceField = null!;
#endif

        // Present in both baseline and current assemblies so Touch can expose
        // a void method's side effect without changing the type layout.
#if DHE_STRUCTURE_CURRENT
        [DheMetadataMarker(2102, "current-field")]
#endif
        public static int TouchValue;

#if DHE_STRUCTURE_CURRENT
		public string RemovedInstanceCounter = null!;

		public int AddedProperty
		{
			get => AddedInstanceCounter;
			set => AddedInstanceCounter = value + 1400;
		}

		public string EvolvedProperty => "property-1500";

		public event Action<int> AddedEvent
		{
			add => TouchValue += 1600;
			remove => TouchValue += 160;
		}

		public event Action<string> EvolvedEvent
		{
			add => TouchValue += 1700;
			remove => TouchValue += 170;
		}

		public int ExerciseCurrentMembers(int value)
		{
			var typeMarker = (DheMetadataMarkerAttribute)Attribute.GetCustomAttribute(
				typeof(DheDemoCalculator), typeof(DheMetadataMarkerAttribute));
			var fieldMarker = (DheMetadataMarkerAttribute)Attribute.GetCustomAttribute(
				typeof(DheDemoCalculator).GetField(nameof(TouchValue)),
				typeof(DheMetadataMarkerAttribute));
			var methodMarker = (DheMetadataMarkerAttribute)Attribute.GetCustomAttribute(
				typeof(DheDemoCalculator).GetMethod(nameof(Stable)),
				typeof(DheMetadataMarkerAttribute));
			if (typeMarker?.Value != 2101 || typeMarker.Label != "current-type" ||
				fieldMarker?.Value != 2102 || fieldMarker.Label != "current-field" ||
				methodMarker?.Value != 2103 || methodMarker.Label != "current-method")
			{
				throw new InvalidOperationException("DHE current custom attribute view is invalid.");
			}
			TouchValue = 0;
			AddedProperty = value;
			Action<int> handler = _ => { };
			AddedEvent += handler;
			AddedEvent -= handler;
			return AddedProperty + TouchValue;
		}

		public int EvolvedInstanceFieldRoundTrip(int value)
		{
			RemovedInstanceCounter = "field-" + value;
			return RemovedInstanceCounter.Length;
		}

		public int ReadEvolvedInstanceField()
		{
			return RemovedInstanceCounter == null ? 0 : RemovedInstanceCounter.Length;
		}
#else
		public int RemovedInstanceCounter = 17;
		public static string RemovedStaticText = "base";

		public int RemovedProperty => 19;

		public int EvolvedProperty => 23;

		public event Action<int> RemovedEvent
		{
			add => TouchValue += 1;
			remove => TouchValue += 1;
		}

		public event Action<int> EvolvedEvent
		{
			add => TouchValue += 2;
			remove => TouchValue += 2;
		}

		public int ReadRemovedFields()
		{
			return RemovedInstanceCounter + RemovedStaticText.Length;
		}
#endif

        public DheDemoCalculator()
        {
#if DHE_BASE2
            // Keep Base 2 observably equivalent while giving its constructor
            // a different method version. The same current payload must then
            // compute a Base-specific changed set at runtime.
            if (TouchValue == int.MinValue)
            {
                TouchValue = 0;
            }
#endif
        }

        public static int Add(int value)
        {
#if DHE_CURRENT
            return value + 100;
#else
            return value + 1;
#endif
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
#if DHE_STRUCTURE_CURRENT
        [DheMetadataMarker(2103, "current-method")]
#endif
        public static int Stable(int value)
        {
            return value * 2;
        }

        public static int AddViaStable(int value)
        {
#if DHE_CURRENT
            return Stable(value) + 100;
#else
            return Stable(value) + 1;
#endif
        }

        public static int AddPair(int left, int right)
        {
#if DHE_CURRENT
            return left + right + 100;
#else
            return left + right + 1;
#endif
        }

        public static long Wide(long value)
        {
#if DHE_CURRENT
            return value + 1000L;
#else
            return value + 1L;
#endif
        }

        public static void Touch(int value)
        {
#if DHE_CURRENT
            TouchValue = value + 700;
#else
            TouchValue = value + 7;
#endif
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int InstanceStable(int value)
        {
            return value * 3;
        }

        public int InstanceAdd(int value)
        {
#if DHE_CURRENT
            return value + 200;
#else
            return value + 2;
#endif
        }

		public int InstanceAddViaStable(int value)
        {
#if DHE_CURRENT
            return InstanceStable(value) + 200;
#else
            return InstanceStable(value) + 2;
#endif
		}

#if DHE_STRUCTURE_CURRENT
		public int SignatureMigrated(long value)
		{
			return checked((int)value + 1300);
		}
#else
		public int RemovedLegacyMethod(int value)
		{
			return value + 13;
		}

		public int SignatureMigrated(int value)
		{
			return value + 13;
		}
#endif

#if DHE_STRUCTURE_CURRENT
#nullable disable
        public static int AddedStaticCounter;
        public static string AddedStaticText;
		public int AddedInstanceCounter;
		public string AddedInstanceText;
		public object AddedInstancePayload;
		public SmallValue AddedInstanceStruct;

        public int AddedInstanceMethod(int value)
        {
            return value + 500;
        }

        public static int AddedStaticFieldRoundTrip(int value)
        {
            AddedStaticCounter = value + 900;
            AddedStaticText = "dhe";
            return ReadAddedStaticFields();
        }

        public static int ReadAddedStaticFields()
        {
            return AddedStaticCounter + (AddedStaticText == null ? 0 : AddedStaticText.Length);
        }

		public int AddedInstanceFieldRoundTrip(int value)
		{
			AddedInstanceCounter = value + 1000;
			AddedInstanceText = "sidecar";
			AddedInstancePayload = new object();
			SmallValue sidecarValue = new SmallValue(value + 10, value + 20);
			AddedInstanceStruct = sidecarValue;
			return ReadAddedInstanceFields();
		}

		public int ReadAddedInstanceFields()
		{
			SmallValue sidecarValue = AddedInstanceStruct;
			return AddedInstanceCounter + (AddedInstanceText == null ? 0 : AddedInstanceText.Length) +
				sidecarValue.Number + (int)sidecarValue.Wide;
		}
#nullable restore
#endif
    }
}
