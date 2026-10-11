namespace RiskOfBonk.Generated
{
    /// <summary>One row of sheets/characters.json.</summary>
    public sealed class CharacterRow
    {
        public readonly string Id, DisplayName, MovesetBody, PrimaryColor, Description;
        public readonly string[] MbNameHints;
        public readonly float SortPosition, HeightMeters, YawOffsetDeg;

        public CharacterRow(string id, string displayName, string[] mbNameHints, string movesetBody, string primaryColor,
            float sortPosition, float heightMeters, float yawOffsetDeg, string description)
        {
            Id = id; DisplayName = displayName; MbNameHints = mbNameHints; MovesetBody = movesetBody; PrimaryColor = primaryColor;
            SortPosition = sortPosition; HeightMeters = heightMeters; YawOffsetDeg = yawOffsetDeg; Description = description;
        }
    }

    /// <summary>One row of sheets/menu_style.json.</summary>
    public sealed class StyleRow
    {
        public readonly string Id, Color;
        public readonly float Alpha;
        public readonly string[] MbSpriteHints;
        public StyleRow(string id, string color, float alpha, string[] mbSpriteHints) { Id = id; Color = color; Alpha = alpha; MbSpriteHints = mbSpriteHints; }
    }

    /// <summary>One row of sheets/stage_modes.json.</summary>
    public sealed class ModeRow
    {
        public readonly string Id, Label, Description, Color;
        public ModeRow(string id, string label, string description, string color) { Id = id; Label = label; Description = description; Color = color; }
    }
}
