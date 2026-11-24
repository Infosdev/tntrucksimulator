using System;
using UnityEngine;

namespace TNLorry.Services
{
    // ── Analytics (section V) ────────────────────────────────────────────────

    public interface IAnalyticsService
    {
        void LogEvent(string name, params (string key, object value)[] parameters);
        void SetUserProperty(string key, string value);
    }

    /// <summary>
    /// Default implementation: logs to the console in development builds, drops events in release.
    /// Replace the registration in Bootstrap with a FirebaseAnalyticsService once the SDK is in.
    /// Call sites never change.
    /// </summary>
    public class NullAnalyticsService : IAnalyticsService
    {
        public void LogEvent(string name, params (string key, object value)[] parameters)
        {
            if (!Debug.isDebugBuild) return;
            var sb = new System.Text.StringBuilder("[Analytics] ").Append(name);
            if (parameters != null && parameters.Length > 0)
            {
                sb.Append(" { ");
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(parameters[i].key).Append('=').Append(parameters[i].value);
                }
                sb.Append(" }");
            }
            Debug.Log(sb.ToString());
        }

        public void SetUserProperty(string key, string value)
        {
            if (Debug.isDebugBuild) Debug.Log("[Analytics] prop " + key + "=" + value);
        }
    }

    // ── Remote Config (section I.2 / U) ─────────────────────────────────────

    public interface IRemoteConfigService
    {
        float GetFloat(string key, float fallback);
        int   GetInt(string key, int fallback);
        bool  GetBool(string key, bool fallback);
        string GetString(string key, string fallback);
    }

    /// <summary>
    /// Offline implementation: always returns the shipped defaults. Keeps every tunable going
    /// through one gate, so turning on Firebase Remote Config later needs no gameplay edits.
    /// </summary>
    public class LocalRemoteConfigService : IRemoteConfigService
    {
        readonly Data.EconomyConfig _economy;

        public LocalRemoteConfigService(Data.EconomyConfig economy) => _economy = economy;

        public float  GetFloat(string key, float fallback)   => fallback;
        public int    GetInt(string key, int fallback)       => fallback;
        public bool   GetBool(string key, bool fallback)     => fallback;
        public string GetString(string key, string fallback) => fallback;

        /// <summary>Shipped economy baseline, used when no remote override exists.</summary>
        public Data.EconomyConfig Economy => _economy;
    }

    // ── Ads (section U) ─────────────────────────────────────────────────────

    public enum RewardedPlacement { DoublePayout, FreeTow, SkipRepairCost, DailyBonus }

    public interface IAdService
    {
        bool IsRewardedReady(RewardedPlacement placement);
        /// <summary>onComplete(true) only when the reward was actually earned.</summary>
        void ShowRewarded(RewardedPlacement placement, Action<bool> onComplete);
        void ShowInterstitialIfAllowed(string context);
        bool AdsRemoved { get; set; }
    }

    /// <summary>
    /// No-SDK stub. Rewarded calls fail closed (onComplete(false)) so the reward path is never
    /// silently granted during development, and interstitials are no-ops.
    /// </summary>
    public class NullAdService : IAdService
    {
        public bool AdsRemoved { get; set; }

        public bool IsRewardedReady(RewardedPlacement placement) => false;

        public void ShowRewarded(RewardedPlacement placement, Action<bool> onComplete)
        {
            Debug.Log("[Ads] Rewarded requested (" + placement + ") but no ad SDK is installed.");
            onComplete?.Invoke(false);
        }

        public void ShowInterstitialIfAllowed(string context) { }
    }
}
