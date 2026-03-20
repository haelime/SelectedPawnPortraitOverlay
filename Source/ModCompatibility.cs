using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class ModCompatibility
{
    private const string FacialAnimationAssemblyName = "FacialAnimation";
    private const string FacialAnimationModTypeName = "FacialAnimation.FacialAnimationMod";
    private const string FacialAnimationUtilModSettingsTypeName = "FacialAnimation.UtilModSettings";
    private const string FacialAnimationSettingsTypeName = "FacialAnimation.FacialAnimationModSettings";
    private const string FacialAnimationSettingsMemberName = "Settings";
    private const string FacialAnimationPlaySettingsMemberName = "PlaySettings";
    private const string PortraitAnimationSettingMemberName = "EnablePortraitAnimationInPortrait";

    private static readonly List<BoolBinding> facialAnimationBindings = new();

    public static bool FacialAnimationActive { get; private set; }
    public static bool CanOverrideFacialAnimationPortraits
    {
        get
        {
            EnsureFacialAnimationBindingsResolved();
            return FacialAnimationActive && facialAnimationBindings.Count > 0;
        }
    }

    public static string FacialAnimationStatusLabel =>
        FacialAnimationActive ? "PortraitOverlay.Settings.FacialAnimationActive" : "PortraitOverlay.Settings.FacialAnimationInactive";

    public static void Initialize()
    {
        FacialAnimationActive = LoadedModManager.RunningModsListForReading.Any(IsFacialAnimationPack);
    }

    public static T WithPortraitFacialAnimationSetting<T>(bool enabled, Func<T> render)
    {
        if (render == null)
        {
            return default;
        }

        EnsureFacialAnimationBindingsResolved();
        if (!CanOverrideFacialAnimationPortraits)
        {
            return render();
        }

        var appliedBindings = new List<(BoolBinding binding, object target, bool originalValue)>();
        foreach (var binding in facialAnimationBindings)
        {
            if (!TryResolveBindingTarget(binding, out var target))
            {
                continue;
            }

            var originalValue = GetBoolMemberValue(binding.TargetMember, target);
            if (originalValue == enabled)
            {
                continue;
            }

            SetMemberValue(binding.TargetMember, target, enabled);
            appliedBindings.Add((binding, target, originalValue));
        }

        try
        {
            return render();
        }
        finally
        {
            foreach (var (binding, target, originalValue) in appliedBindings)
            {
                SetMemberValue(binding.TargetMember, target, originalValue);
            }
        }
    }

    private static void EnsureFacialAnimationBindingsResolved()
    {
        if (!FacialAnimationActive || facialAnimationBindings.Count > 0)
        {
            return;
        }

        ResolveFacialAnimationMembers();
    }

    public static T WithPortraitWeaponRenderMode<T>(Func<T> render)
    {
        if (render == null)
        {
            return default;
        }

        var originalMode = Prefs.ShowWeaponsUnderPortraitMode;
        if (originalMode == ShowWeaponsUnderPortraitMode.WhileDrafted)
        {
            return render();
        }

        Prefs.ShowWeaponsUnderPortraitMode = ShowWeaponsUnderPortraitMode.WhileDrafted;
        try
        {
            return render();
        }
        finally
        {
            Prefs.ShowWeaponsUnderPortraitMode = originalMode;
        }
    }

    public static T WithPortraitHeadgearPreference<T>(bool renderHeadgear, Func<T> render)
    {
        if (render == null)
        {
            return default;
        }

        var originalValue = Prefs.HatsOnlyOnMap;
        if (!renderHeadgear || !originalValue)
        {
            return render();
        }

        Prefs.HatsOnlyOnMap = false;
        try
        {
            return render();
        }
        finally
        {
            Prefs.HatsOnlyOnMap = originalValue;
        }
    }

    private static void ResolveFacialAnimationMembers()
    {
        facialAnimationBindings.Clear();

        if (!FacialAnimationActive)
        {
            return;
        }

        var facialAnimationAssembly = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, FacialAnimationAssemblyName, StringComparison.Ordinal));
        if (facialAnimationAssembly == null)
        {
            return;
        }

        var facialAnimationModType = facialAnimationAssembly.GetType(FacialAnimationModTypeName);
        var facialAnimationUtilModSettingsType = facialAnimationAssembly.GetType(FacialAnimationUtilModSettingsTypeName);
        var facialAnimationSettingsType = facialAnimationAssembly.GetType(FacialAnimationSettingsTypeName)
            ?? GetTypesSafely(facialAnimationAssembly).FirstOrDefault(type => type?.Name == "FacialAnimationModSettings");
        if (facialAnimationSettingsType == null)
        {
            return;
        }

        var uniqueBindingKeys = new HashSet<string>(StringComparer.Ordinal);
        if (TryAddExactBinding(facialAnimationModType, facialAnimationSettingsType, uniqueBindingKeys))
        {
            return;
        }

        AddBindingsForType(facialAnimationModType, facialAnimationSettingsType, uniqueBindingKeys);
        AddBindingsForType(facialAnimationUtilModSettingsType, facialAnimationSettingsType, uniqueBindingKeys);

        foreach (var candidateType in GetTypesSafely(facialAnimationAssembly))
        {
            if (candidateType == facialAnimationModType || candidateType == facialAnimationUtilModSettingsType)
            {
                continue;
            }

            AddBindingsForType(candidateType, facialAnimationSettingsType, uniqueBindingKeys);
        }
    }

    private static bool TryAddExactBinding(Type modType, Type settingsType, HashSet<string> uniqueBindingKeys)
    {
        if (modType == null || settingsType == null)
        {
            return false;
        }

        var settingsField = modType.GetField(
            FacialAnimationSettingsMemberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        var portraitField = settingsType.GetField(
            PortraitAnimationSettingMemberName,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (settingsField == null || portraitField == null)
        {
            return false;
        }

        AddBinding(new BoolBinding(settingsField, portraitField), uniqueBindingKeys);
        return facialAnimationBindings.Count > 0;
    }

    private static IEnumerable<Type> GetTypesSafely(Assembly assembly)
    {
        if (assembly == null)
        {
            return Enumerable.Empty<Type>();
        }

        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type != null);
        }
    }

    private static void AddBindingsForType(Type declaringType, Type settingsType, HashSet<string> uniqueBindingKeys)
    {
        if (declaringType == null || settingsType == null)
        {
            return;
        }

        foreach (var binding in FindDirectStaticBindings(declaringType))
        {
            AddBinding(binding, uniqueBindingKeys);
        }

        foreach (var providerMember in GetReadableStaticMembers(declaringType))
        {
            if (IsReferenceType(providerMember) && GetMemberType(providerMember) == settingsType)
            {
                AddBinding(new BoolBinding(providerMember, FindBoolMember(settingsType, PortraitAnimationSettingMemberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)), uniqueBindingKeys);
            }

            var providerType = GetMemberType(providerMember);
            if (providerType == null)
            {
                continue;
            }

            foreach (var nestedMember in GetReadableInstanceMembers(providerType))
            {
                var nestedType = GetMemberType(nestedMember);
                if (nestedType == null)
                {
                    continue;
                }

                if (nestedType == settingsType
                    || nestedMember.Name.IndexOf(FacialAnimationPlaySettingsMemberName, StringComparison.OrdinalIgnoreCase) >= 0
                    || nestedType.Name.IndexOf("Settings", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    AddBinding(new BoolBinding(providerMember, nestedMember, FindBoolMember(nestedType, PortraitAnimationSettingMemberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)), uniqueBindingKeys);
                }
            }
        }
    }

    private static IEnumerable<BoolBinding> FindDirectStaticBindings(Type declaringType)
    {
        var directBinding = FindBoolMember(declaringType, PortraitAnimationSettingMemberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (directBinding != null)
        {
            yield return new BoolBinding(directBinding);
        }
    }

    private static IEnumerable<MemberInfo> GetReadableStaticMembers(Type declaringType)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        foreach (var field in declaringType.GetFields(flags))
        {
            if (IsReadableMember(field, field.FieldType, isStatic: true))
            {
                yield return field;
            }
        }

        foreach (var property in declaringType.GetProperties(flags))
        {
            if (IsReadableMember(property, property.PropertyType, isStatic: true))
            {
                yield return property;
            }
        }
    }

    private static IEnumerable<MemberInfo> GetReadableInstanceMembers(Type declaringType)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        foreach (var field in declaringType.GetFields(flags))
        {
            if (IsReadableMember(field, field.FieldType, isStatic: false))
            {
                yield return field;
            }
        }

        foreach (var property in declaringType.GetProperties(flags))
        {
            if (IsReadableMember(property, property.PropertyType, isStatic: false))
            {
                yield return property;
            }
        }
    }

    private static MemberInfo FindBoolMember(Type declaringType, string memberName, BindingFlags flags)
    {
        var exactField = declaringType.GetField(memberName, flags);
        if (IsWritableBoolMember(exactField, flags))
        {
            return exactField;
        }

        var exactProperty = declaringType.GetProperty(memberName, flags);
        if (IsWritableBoolMember(exactProperty, flags))
        {
            return exactProperty;
        }

        foreach (var field in declaringType.GetFields(flags))
        {
            if (field.FieldType == typeof(bool)
                && field.Name.IndexOf(memberName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return field;
            }
        }

        foreach (var property in declaringType.GetProperties(flags))
        {
            if (property.PropertyType == typeof(bool)
                && property.CanRead
                && property.CanWrite
                && property.Name.IndexOf(memberName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return property;
            }
        }

        return null;
    }

    private static void AddBinding(BoolBinding binding, HashSet<string> uniqueBindingKeys)
    {
        if (binding?.TargetMember == null)
        {
            return;
        }

        var key = binding.BindingKey;
        if (!uniqueBindingKeys.Add(key))
        {
            return;
        }

        facialAnimationBindings.Add(binding);
    }

    private static bool TryResolveBindingTarget(BoolBinding binding, out object target)
    {
        target = null;

        if (binding == null || binding.TargetMember == null)
        {
            return false;
        }

        if (binding.ProviderMember == null)
        {
            return true;
        }

        var providerValue = GetMemberValue(binding.ProviderMember, null);
        if (providerValue == null)
        {
            return false;
        }

        if (binding.NestedProviderMember == null)
        {
            target = providerValue;
            return true;
        }

        var nestedValue = GetMemberValue(binding.NestedProviderMember, providerValue);
        if (nestedValue == null)
        {
            return false;
        }

        target = nestedValue;
        return true;
    }

    private static bool IsReadableMember(MemberInfo member, Type expectedType, bool isStatic)
    {
        return member switch
        {
            FieldInfo field => field.FieldType == expectedType && field.IsStatic == isStatic,
            PropertyInfo property => property.PropertyType == expectedType
                && property.CanRead
                && property.GetGetMethod(nonPublic: true)?.IsStatic == isStatic,
            _ => false
        };
    }

    private static Type GetMemberType(MemberInfo member)
    {
        return member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property => property.PropertyType,
            _ => null
        };
    }

    private static bool IsReferenceType(MemberInfo member)
    {
        var memberType = GetMemberType(member);
        return memberType != null && !memberType.IsValueType && memberType != typeof(string);
    }

    private static bool IsWritableBoolMember(MemberInfo member, BindingFlags flags)
    {
        return member switch
        {
            FieldInfo field => field.FieldType == typeof(bool) && field.IsStatic == flags.HasFlag(BindingFlags.Static),
            PropertyInfo property => property.PropertyType == typeof(bool)
                && property.CanRead
                && property.CanWrite
                && property.GetGetMethod(nonPublic: true)?.IsStatic == flags.HasFlag(BindingFlags.Static)
                && property.GetSetMethod(nonPublic: true) != null,
            _ => false
        };
    }

    private static object GetMemberValue(MemberInfo member, object instance)
    {
        return member switch
        {
            FieldInfo field => field.GetValue(instance),
            PropertyInfo property => property.GetValue(instance),
            _ => null
        };
    }

    private static bool GetBoolMemberValue(MemberInfo member, object instance)
    {
        return GetMemberValue(member, instance) is bool value && value;
    }

    private static void SetMemberValue(MemberInfo member, object instance, object value)
    {
        switch (member)
        {
            case FieldInfo field:
                field.SetValue(instance, value);
                break;
            case PropertyInfo property:
                property.SetValue(instance, value);
                break;
        }
    }

    private sealed class BoolBinding
    {
        public BoolBinding(MemberInfo targetMember)
        {
            TargetMember = targetMember;
        }

        public BoolBinding(MemberInfo providerMember, MemberInfo targetMember)
        {
            ProviderMember = providerMember;
            TargetMember = targetMember;
        }

        public BoolBinding(MemberInfo providerMember, MemberInfo nestedProviderMember, MemberInfo targetMember)
        {
            ProviderMember = providerMember;
            NestedProviderMember = nestedProviderMember;
            TargetMember = targetMember;
        }

        public MemberInfo ProviderMember { get; }
        public MemberInfo NestedProviderMember { get; }
        public MemberInfo TargetMember { get; }

        public string BindingKey =>
            string.Join(
                "|",
                DescribeMember(ProviderMember),
                DescribeMember(NestedProviderMember),
                DescribeMember(TargetMember));

        private static string DescribeMember(MemberInfo member)
        {
            return member == null ? "<null>" : member.DeclaringType?.FullName + "." + member.Name;
        }
    }

    private static bool IsFacialAnimationPack(ModContentPack pack)
    {
        if (pack == null)
        {
            return false;
        }

        return Contains(pack.Name, "Facial Animation")
            || Contains(pack.PackageIdPlayerFacing, "facial");
    }

    private static bool Contains(string source, string value)
    {
        return !string.IsNullOrEmpty(source)
            && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
