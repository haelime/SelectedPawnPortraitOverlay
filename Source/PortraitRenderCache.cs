using System.Collections;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class PortraitRenderCache
{
    private const string FacialAnimationAssemblyName = "FacialAnimation";
    private const string FacialAnimationDirtyMethodName = "SetDirty";

    private static readonly FieldInfo CachedPortraitsField =
        typeof(PortraitsCache).GetField("cachedPortraits", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly PropertyInfo CachedPortraitRenderTextureProperty =
        typeof(PortraitsCache).GetNestedType("CachedPortrait", BindingFlags.NonPublic)
            ?.GetProperty("RenderTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly MethodInfo DestroyRenderTextureMethod =
        typeof(PortraitsCache).GetMethod("DestroyRenderTexture", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly MethodInfo SetAnimatedPortraitsDirtyMethod =
        typeof(PortraitsCache).GetMethod("SetAnimatedPortraitsDirty", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

    public static Texture GetPortrait(Pawn pawn, Vector2 size, PortraitOverlaySettings settings)
    {
        if (pawn == null)
        {
            return BaseContent.BadTex;
        }

        try
        {
            return ModCompatibility.WithPortraitWeaponRenderMode(
                () => ModCompatibility.WithPortraitHeadgearPreference(
                    settings.RenderHeadgear,
                    () => ModCompatibility.WithPortraitFacialAnimationSetting(
                        settings.EnableFacialAnimationInOverlayPortrait,
                        () =>
                        {
                            InvalidatePortraitState(pawn);
                            var portrait = PawnFullBodyPortraitRenderer.Render(pawn, size, settings);
                            InvalidatePortraitState(pawn);
                            return portrait;
                        })));
        }
        finally
        {
            InvalidatePortraitState(pawn);
        }
    }

    private static void InvalidatePortraitState(Pawn pawn)
    {
        if (pawn == null)
        {
            return;
        }

        InvalidateFacialAnimationState(pawn);
        SetAnimatedPortraitsDirtyMethod?.Invoke(null, null);
        RemoveCachedPortraits(pawn);
        PortraitsCache.SetDirty(pawn);
    }

    private static void RemoveCachedPortraits(Pawn pawn)
    {
        if (pawn == null || CachedPortraitsField?.GetValue(null) is not IDictionary cachedPortraits)
        {
            return;
        }

        foreach (DictionaryEntry portraitCacheEntry in cachedPortraits)
        {
            if (portraitCacheEntry.Value is not IDictionary pawnPortraits || !pawnPortraits.Contains(pawn))
            {
                continue;
            }

            DestroyCachedPortraitTexture(pawnPortraits[pawn]);
            pawnPortraits.Remove(pawn);
        }
    }

    private static void DestroyCachedPortraitTexture(object cachedPortrait)
    {
        if (cachedPortrait == null
            || CachedPortraitRenderTextureProperty?.GetValue(cachedPortrait) is not RenderTexture renderTexture
            || DestroyRenderTextureMethod == null)
        {
            return;
        }

        DestroyRenderTextureMethod.Invoke(null, new object[] { renderTexture });
    }

    private static void InvalidateFacialAnimationState(Pawn pawn)
    {
        if (pawn?.AllComps == null)
        {
            return;
        }

        foreach (var comp in pawn.AllComps)
        {
            if (comp == null
                || !string.Equals(comp.GetType().Assembly.GetName().Name, FacialAnimationAssemblyName, System.StringComparison.Ordinal))
            {
                continue;
            }

            var setDirtyMethod = comp.GetType().GetMethod(
                FacialAnimationDirtyMethodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                System.Type.EmptyTypes,
                null);
            if (setDirtyMethod == null)
            {
                continue;
            }

            setDirtyMethod.Invoke(comp, null);
        }
    }
}
