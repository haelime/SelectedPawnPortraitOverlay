using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace SelectedPawnPortraitOverlay;

public static class PawnFullBodyPortraitRenderer
{
    private static readonly MethodInfo PortraitGetMethod = ResolvePortraitGetMethod();
    private const float MinimumBodySize = 0.15f;
    private const float MinimumBodyScaleFactor = 0.75f;
    private const float MaximumBodyScaleFactor = 1.35f;
    private const float FaceEmphasisVerticalOffset = 0.08f;
    private const float FaceEmphasisZoomFactor = 0.02f;
    private const float MinimumEffectiveZoom = 0.65f;
    private const float MaximumEffectiveZoom = 1.95f;

    public static Texture Render(Pawn pawn, Vector2 size, PortraitOverlaySettings settings)
    {
        if (pawn == null || PortraitGetMethod == null)
        {
            return BaseContent.BadTex;
        }

        var rotation = settings.LivePortrait ? pawn.Rotation : Rot4.South;
        var bodyScaleFactor = GetBodyScaleFactor(pawn);
        var baseZoom = settings.CameraZoom * bodyScaleFactor;
        var cameraOffset = Vector3.zero;
        var zoom = baseZoom;

        if (!settings.RenderHeadgear)
        {
            cameraOffset = new Vector3(0f, settings.FaceEmphasis * FaceEmphasisVerticalOffset, 0f);
            zoom += settings.FaceEmphasis * FaceEmphasisZoomFactor;
        }

        zoom = Mathf.Clamp(zoom, MinimumEffectiveZoom, MaximumEffectiveZoom);
        var args = BuildArguments(PortraitGetMethod.GetParameters(), pawn, size, rotation, cameraOffset, zoom, settings);
        var texture = PortraitGetMethod.Invoke(null, args) as Texture;
        return texture ?? BaseContent.BadTex;
    }

    private static float GetBodyScaleFactor(Pawn pawn)
    {
        var normalizedBodySize = Mathf.Max(MinimumBodySize, pawn.BodySize);
        var scaleFactor = Mathf.Sqrt(1f / normalizedBodySize);
        return Mathf.Clamp(scaleFactor, MinimumBodyScaleFactor, MaximumBodyScaleFactor);
    }

    private static MethodInfo ResolvePortraitGetMethod()
    {
        foreach (var method in typeof(PortraitsCache).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.Name != "Get")
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length >= 5
                && parameters[0].ParameterType == typeof(Pawn)
                && parameters[1].ParameterType == typeof(Vector2))
            {
                return method;
            }
        }

        return null;
    }

    private static object[] BuildArguments(
        ParameterInfo[] parameters,
        Pawn pawn,
        Vector2 size,
        Rot4 rotation,
        Vector3 cameraOffset,
        float zoom,
        PortraitOverlaySettings settings)
    {
        var args = new object[parameters.Length];
        var boolIndex = 0;

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var type = parameter.ParameterType;

            if (type == typeof(Pawn))
            {
                args[i] = pawn;
                continue;
            }

            if (type == typeof(Vector2))
            {
                args[i] = size;
                continue;
            }

            if (type == typeof(Rot4))
            {
                args[i] = rotation;
                continue;
            }

            if (type == typeof(Vector3))
            {
                args[i] = cameraOffset;
                continue;
            }

            if (type == typeof(float))
            {
                args[i] = zoom;
                continue;
            }

            if (type == typeof(bool))
            {
                args[i] = ResolveBoolArgument(parameter.Name, boolIndex, settings);
                boolIndex++;
                continue;
            }

            if (parameter.HasDefaultValue)
            {
                args[i] = parameter.DefaultValue;
                continue;
            }

            if (type.IsValueType)
            {
                args[i] = Activator.CreateInstance(type);
                continue;
            }

            if (typeof(IDictionary).IsAssignableFrom(type)
                || type == typeof(List<Color>)
                || type == typeof(string))
            {
                args[i] = null;
                continue;
            }

            args[i] = null;
        }

        return args;
    }

    private static bool ResolveBoolArgument(string parameterName, int boolIndex, PortraitOverlaySettings settings)
    {
        if (!string.IsNullOrEmpty(parameterName))
        {
            return parameterName switch
            {
                "supersample" => true,
                "compensateForUIScale" => true,
                "renderHeadgear" => settings.RenderHeadgear,
                "renderClothes" => settings.RenderApparel,
                "stylingStation" => false,
                _ => ResolveBoolArgumentByIndex(boolIndex, settings)
            };
        }

        return ResolveBoolArgumentByIndex(boolIndex, settings);
    }

    private static bool ResolveBoolArgumentByIndex(int boolIndex, PortraitOverlaySettings settings)
    {
        return boolIndex switch
        {
            0 => true,
            1 => true,
            2 => settings.RenderHeadgear,
            3 => settings.RenderApparel,
            _ => false
        };
    }
}
