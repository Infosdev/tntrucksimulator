using UnityEngine;

namespace TNLorry.Services
{
    public enum DeviceTierLevel { Low = 0, Mid = 1, High = 2 }

    /// <summary>
    /// Heuristic device classification (section H.1). Deliberately conservative:
    /// misjudging a cheap phone as High costs us a 1-star review, misjudging a flagship
    /// as Mid costs nobody anything. Players can always override in Settings.
    /// </summary>
    public static class DeviceTier
    {
        public static DeviceTierLevel Detect()
        {
            int ramMb = SystemInfo.systemMemorySize;        // 0 on some devices
            int cores = SystemInfo.processorCount;
            int gfxMb = SystemInfo.graphicsMemorySize;

            if (ramMb <= 0) ramMb = 3000;                   // unknown -> assume low-mid

            if (ramMb >= 7000 && cores >= 8 && gfxMb >= 2000) return DeviceTierLevel.High;
            if (ramMb >= 3800 && cores >= 6)                 return DeviceTierLevel.Mid;
            return DeviceTierLevel.Low;
        }
    }

    /// <summary>
    /// Applies the per-tier render/physics budget. URP asset swapping is done by index into
    /// QualitySettings, so the three URP assets in Settings/ must be registered in the
    /// Quality levels list in the same Low/Mid/High order.
    /// </summary>
    public class QualityService
    {
        public DeviceTierLevel DetectedTier { get; }
        public DeviceTierLevel ActiveTier   { get; private set; }

        /// <summary>Tile streaming radius and LOD bias read this.</summary>
        public float DrawDistance { get; private set; } = 600f;
        public int   TargetFps    { get; private set; } = 30;

        public QualityService(DeviceTierLevel detected)
        {
            DetectedTier = detected;
            ActiveTier   = detected;
        }

        /// <summary>savedTier &lt; 0 means "auto"; otherwise the player's explicit choice wins.</summary>
        public void Apply(int savedTier)
        {
            ActiveTier = savedTier < 0
                ? DetectedTier
                : (DeviceTierLevel)Mathf.Clamp(savedTier, 0, 2);

            switch (ActiveTier)
            {
                case DeviceTierLevel.Low:
                    QualitySettings.SetQualityLevel(0, true);
                    Time.fixedDeltaTime   = 0.02f;          // 50 Hz physics (section K.1)
                    DrawDistance          = 450f;
                    TargetFps             = 30;
                    QualitySettings.shadows            = ShadowQuality.Disable;
                    QualitySettings.lodBias            = 0.6f;
                    QualitySettings.skinWeights        = SkinWeights.TwoBones;
                    QualitySettings.globalTextureMipmapLimit = 1;   // half-res textures
                    break;

                case DeviceTierLevel.Mid:
                    QualitySettings.SetQualityLevel(1, true);
                    Time.fixedDeltaTime   = 0.02f;
                    DrawDistance          = 700f;
                    TargetFps             = 45;
                    QualitySettings.shadows            = ShadowQuality.HardOnly;
                    QualitySettings.lodBias            = 1.0f;
                    QualitySettings.globalTextureMipmapLimit = 0;
                    break;

                default:
                    QualitySettings.SetQualityLevel(2, true);
                    Time.fixedDeltaTime   = 0.0167f;        // 60 Hz physics
                    DrawDistance          = 1100f;
                    TargetFps             = 60;
                    QualitySettings.shadows            = ShadowQuality.All;
                    QualitySettings.lodBias            = 1.4f;
                    QualitySettings.globalTextureMipmapLimit = 0;
                    break;
            }

            Application.targetFrameRate = TargetFps;
            Debug.Log("[QualityService] Applied " + ActiveTier + " (target " + TargetFps + " fps, draw " + DrawDistance + "m)");
        }
    }
}
