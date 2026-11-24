using TNLorry.Core;
using TNLorry.Data;
using UnityEngine;

namespace TNLorry.Gameplay
{
    /// <summary>
    /// A generated delivery contract (section K.3). Runtime-only: the persisted form is
    /// ActiveJobSave, which stores ids and is rehydrated through FromSave.
    /// </summary>
    public class Job
    {
        public string    Id;
        public CargoData Cargo;
        public CityData  OriginCity;
        public CityData  DestinationCity;
        public float     DistanceKm;
        public float     TimeLimitMinutes;
        public int       Payout;
        public bool      IsGhatRoute;

        /// <summary>0..1. Accumulated by DamageSystem from shocks, collisions and rain exposure.</summary>
        public float CargoDamage01;
        public float ElapsedMinutes;
        public bool  CargoLoaded;

        public string OriginNameKey      => OriginCity != null ? OriginCity.nameKey : "";
        public string DestinationNameKey => DestinationCity != null ? DestinationCity.nameKey : "";
        public float  MassKg             => Cargo != null ? Cargo.massKg : 0f;
        public bool   IsOverdue          => ElapsedMinutes > TimeLimitMinutes;
        public float  MinutesRemaining   => Mathf.Max(0f, TimeLimitMinutes - ElapsedMinutes);

        public ActiveJobSave ToSave() => new ActiveJobSave
        {
            hasJob          = true,
            jobId           = Id,
            cargoId         = Cargo != null ? Cargo.id : "",
            fromCityId      = OriginCity != null ? OriginCity.id : "",
            toCityId        = DestinationCity != null ? DestinationCity.id : "",
            distanceKm      = DistanceKm,
            timeLimitMinutes = TimeLimitMinutes,
            elapsedMinutes  = ElapsedMinutes,
            payout          = Payout,
            cargoDamage     = CargoDamage01,
            cargoLoaded     = CargoLoaded
        };

        /// <summary>Returns null when the save has no active job or references content this build lacks.</summary>
        public static Job FromSave(ActiveJobSave save, GameDatabase db)
        {
            if (save == null || !save.hasJob) return null;

            var cargo = db.GetCargo(save.cargoId);
            var from  = db.GetCity(save.fromCityId);
            var to    = db.GetCity(save.toCityId);

            if (cargo == null || from == null || to == null)
            {
                Debug.LogWarning("[Job] Saved job " + save.jobId + " references missing content " +
                                 "(cargo=" + save.cargoId + ", from=" + save.fromCityId + ", to=" + save.toCityId +
                                 "); dropping it.");
                return null;
            }

            return new Job
            {
                Id               = save.jobId,
                Cargo            = cargo,
                OriginCity       = from,
                DestinationCity  = to,
                DistanceKm       = save.distanceKm,
                TimeLimitMinutes = save.timeLimitMinutes,
                ElapsedMinutes   = save.elapsedMinutes,
                Payout           = save.payout,
                CargoDamage01    = save.cargoDamage,
                CargoLoaded      = save.cargoLoaded,
                IsGhatRoute      = false      // recomputed by the router when the route is rebuilt
            };
        }
    }
}
