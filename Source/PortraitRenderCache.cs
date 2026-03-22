using System;
using System.Collections;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class PortraitRenderCache
{
    private const int PortraitRenderWarningId = 18467233;

    private static readonly FieldInfo CachedPortraitsField =
        typeof(PortraitsCache).GetField("cachedPortraits", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly PropertyInfo CachedPortraitRenderTextureProperty =
        typeof(PortraitsCache).GetNestedType("CachedPortrait", BindingFlags.NonPublic)
            ?.GetProperty("RenderTexture", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly MethodInfo DestroyRenderTextureMethod =
        typeof(PortraitsCache).GetMethod("DestroyRenderTexture", BindingFlags.Static | BindingFlags.NonPublic);

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
                    () =>
                    {
                        InvalidatePortraitState(pawn);
                        var portrait = PawnFullBodyPortraitRenderer.Render(pawn, size, settings);
                        InvalidatePortraitState(pawn);
                        return portrait;
                    }));
        }
        catch (Exception exception)
        {
            var warningKey = PortraitRenderWarningId ^ (pawn.GetType().FullName?.GetHashCode() ?? 0);
            Log.WarningOnce(
                $"Selected Pawn Portrait Overlay skipped portrait rendering for unsupported pawn type '{pawn.GetType().FullName}'. {exception.GetType().Name}: {exception.Message}",
                warningKey);
            return BaseContent.BadTex;
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
}
