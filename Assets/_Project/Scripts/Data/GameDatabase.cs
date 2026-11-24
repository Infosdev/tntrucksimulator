using System.Collections.Generic;
using UnityEngine;

namespace TNLorry.Data
{
    /// <summary>
    /// Id -> asset lookup for everything data-driven. One asset, assigned in Bootstrap and the
    /// job board, so nothing has to go through Resources.FindObjectsOfType or string paths.
    ///
    /// When the content set grows past Phase 1, swap the direct references for Addressable
    /// AssetReferences and load on demand — the GetX(id) API stays the same.
    /// </summary>
    [CreateAssetMenu(menuName = "TN/Game Database", fileName = "GameDatabase")]
    public class GameDatabase : ScriptableObject
    {
        public TruckData[] trucks = new TruckData[0];
        public CargoData[] cargo  = new CargoData[0];
        public CityData[]  cities = new CityData[0];

        Dictionary<string, TruckData> _trucksById;
        Dictionary<string, CargoData> _cargoById;
        Dictionary<string, CityData>  _citiesById;

        public void Build()
        {
            _trucksById = Index(trucks, t => t.id);
            _cargoById  = Index(cargo,  c => c.id);
            _citiesById = Index(cities, c => c.id);
        }

        Dictionary<string, T> Index<T>(T[] source, System.Func<T, string> keySelector) where T : ScriptableObject
        {
            var map = new Dictionary<string, T>(source.Length);
            foreach (var item in source)
            {
                if (item == null) continue;
                var key = keySelector(item);
                if (string.IsNullOrEmpty(key))
                {
                    Debug.LogError("[GameDatabase] " + item.name + " has an empty id.", item);
                    continue;
                }
                if (map.ContainsKey(key))
                {
                    Debug.LogError("[GameDatabase] Duplicate id '" + key + "' (" + item.name + ").", item);
                    continue;
                }
                map[key] = item;
            }
            return map;
        }

        public TruckData GetTruck(string id) => Lookup(ref _trucksById, trucks, t => t.id, id);
        public CargoData GetCargo(string id) => Lookup(ref _cargoById,  cargo,  c => c.id, id);
        public CityData  GetCity (string id) => Lookup(ref _citiesById, cities, c => c.id, id);

        T Lookup<T>(ref Dictionary<string, T> map, T[] source, System.Func<T, string> keySelector, string id)
            where T : ScriptableObject
        {
            if (map == null) map = Index(source, keySelector);
            if (string.IsNullOrEmpty(id)) return null;
            return map.TryGetValue(id, out var found) ? found : null;
        }

        /// <summary>Cities the player has unlocked, in database order.</summary>
        public List<CityData> UnlockedCities(IList<string> unlockedIds)
        {
            var result = new List<CityData>(unlockedIds.Count);
            foreach (var city in cities)
                if (city != null && unlockedIds.Contains(city.id)) result.Add(city);
            return result;
        }

        public List<CargoData> AvailableCargo(int playerLevel, IList<string> unlockedIds)
        {
            var result = new List<CargoData>();
            foreach (var c in cargo)
                if (c != null && c.unlockLevel <= playerLevel && unlockedIds.Contains(c.id)) result.Add(c);
            return result;
        }
    }
}
