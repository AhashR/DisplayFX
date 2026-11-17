using System;
using System.Collections.Generic;
using System.Linq;

namespace DisplayFX.Objects.Entities;

public sealed record ProfileIcon(string Id, string Glyph, string Label);
public static class ProfileIcons
{
    public const string DefaultId = "monitor";
    public static IReadOnlyList<ProfileIcon> Choices { get; } = Array.AsReadOnly(new[]
    {
        new ProfileIcon(DefaultId, "\uE7F4", "Monitor"),
        new ProfileIcon("game", "\uE7FC", "Game controller"),
        new ProfileIcon("target", "\uE7C8", "Target"),
        new ProfileIcon("moon", "\uE708", "Moon"),
        new ProfileIcon("sun", "\uE706", "Sun"),
        new ProfileIcon("film", "\uE8B2", "Film"),
        new ProfileIcon("work", "\uE821", "Briefcase"),
        new ProfileIcon("music", "\uE8D6", "Music"),
        new ProfileIcon("camera", "\uE722", "Camera"),
        new ProfileIcon("star", "\uE735", "Star")
    });
    public static string Normalize(string? id) => Choices.Any(icon => icon.Id == id) ? id! : DefaultId;
    public static string Glyph(string? id) => Choices.First(icon => icon.Id == Normalize(id)).Glyph;
}
