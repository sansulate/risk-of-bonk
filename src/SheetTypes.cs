namespace RiskOfBonk.Generated
{
    /// <summary>One row of sheets/characters.json.</summary>
    public sealed class CharacterRow
    {
        public readonly string Id, DisplayName, MbPortrait, MovesetBody, PrimaryColor, Description;
        /// <summary>Optional exact Megabonk texture for the body, when the model's own material is another skin's.</summary>
        public readonly string MbTexture;
        /// <summary>Megabonk weapon id (weapons.json) on the primary slot.</summary>
        public readonly string StartWeapon;
        public readonly string[] MbNameHints;
        public readonly float SortPosition, HeightMeters, YawOffsetDeg;

        public CharacterRow(string id, string displayName, string[] mbNameHints, string mbPortrait, string movesetBody, string primaryColor,
            float sortPosition, float heightMeters, float yawOffsetDeg, string description, string mbTexture, string startWeapon = "")
        {
            Id = id; DisplayName = displayName; MbNameHints = mbNameHints; MbPortrait = mbPortrait; MovesetBody = movesetBody; PrimaryColor = primaryColor;
            SortPosition = sortPosition; HeightMeters = heightMeters; YawOffsetDeg = yawOffsetDeg; Description = description; MbTexture = mbTexture; StartWeapon = startWeapon;
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

    /// <summary>One row of sheets/upgrades.json (a Megabonk tome).</summary>
    public sealed class UpgradeRow
    {
        public readonly string Id, DisplayName, MbIcon, Stat, Description;
        public readonly float PerPoint;
        public UpgradeRow(string id, string displayName, string mbIcon, string stat, float perPoint, string description)
        { Id = id; DisplayName = displayName; MbIcon = mbIcon; Stat = stat; PerPoint = perPoint; Description = description; }
    }

    /// <summary>One row of sheets/rarities.json.</summary>
    public sealed class RarityRow
    {
        public readonly string Id, Label, Color;
        public readonly float Weight;
        public readonly int Points;
        public RarityRow(string id, string label, string color, float weight, int points) { Id = id; Label = label; Color = color; Weight = weight; Points = points; }
    }

    /// <summary>One row of sheets/bosses.json (a Megabonk boss on a Risk of Rain 2 boss body).</summary>
    public sealed class BossRow
    {
        public readonly string Id, DisplayName, Subtitle, MbTexture, RorBody, RorMaster, Role;
        public readonly string[] MbNameHints, Replaces;
        public readonly bool Airborne, Retarget;
        public readonly int HealthBoost;
        public BossRow(string id, string displayName, string subtitle, string[] mbNameHints, string mbTexture, string rorBody, string rorMaster, bool airborne, int healthBoost,
            string role, string[] replaces, bool retarget)
        {
            Role = role; Replaces = replaces; Retarget = retarget;
            Id = id; DisplayName = displayName; Subtitle = subtitle; MbNameHints = mbNameHints; MbTexture = mbTexture;
            RorBody = rorBody; RorMaster = rorMaster; Airborne = airborne; HealthBoost = healthBoost;
        }
    }

    /// <summary>One row of sheets/weapons.json (a Megabonk weapon as a skill).</summary>
    public sealed class WeaponRow
    {
        public readonly string Id, DisplayName, MbIcon, Kind, Prefab, Effect, DamageType, Sound, Description;
        /// <summary>Megabonk AudioClip for the attack, and the Megabonk model a projectile shows (with its scale and spin).</summary>
        public readonly string MbSound, MbProjectile;
        public readonly float ProjScale, ProjSpin;
        public readonly float Damage, Interval, Cooldown, Spread, Range;
        public readonly int Count;
        public readonly bool Pierce;
        public WeaponRow(string id, string displayName, string mbIcon, string kind, float damage, float interval, float cooldown, int count, float spread,
            float range, string prefab, string effect, string damageType, bool pierce, string sound, string description,
            string mbSound = "", string mbProjectile = "", float projScale = 1f, float projSpin = 0f)
        {
            Id = id; DisplayName = displayName; MbIcon = mbIcon; Kind = kind; Damage = damage; Interval = interval; Cooldown = cooldown; Count = count;
            Spread = spread; Range = range; Prefab = prefab; Effect = effect; DamageType = damageType; Pierce = pierce; Sound = sound; Description = description;
            MbSound = mbSound; MbProjectile = mbProjectile; ProjScale = projScale; ProjSpin = projSpin;
        }
    }
    /// <summary>One row of sheets/animations.json: a clip role and the words that pick a Megabonk clip for it.</summary>
    public sealed class AnimRow
    {
        public readonly string Id;
        public readonly string[] ClipWords;
        public AnimRow(string id, string[] clipWords) { Id = id; ClipWords = clipWords; }
    }
    /// <summary>One row of sheets/sounds.json.</summary>
    public sealed class SoundRow
    {
        public readonly string Id, MbSound;
        public SoundRow(string id, string mbSound) { Id = id; MbSound = mbSound; }
    }
}
