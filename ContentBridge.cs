using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using RaidPrototype.Content;

namespace RaidPrototype
{
    /// <summary>
    /// Host-approved data transport: the game loads CSV, framework reads the loaded dictionary.
    /// Independent packs contain no reference to the framework assembly.
    /// </summary>
    internal static class ContentBridge
    {
        internal const string PayloadKey="raid_framework_pack_v1";
        private const string FrameworkId="local.raidprototype";
        private sealed class Binding { internal LoadedMod Provider; internal string PackId; internal string Payload; }
        private static readonly Dictionary<string,Binding> bindings=new Dictionary<string,Binding>(StringComparer.Ordinal);
        private static readonly Dictionary<string,string> failures=new Dictionary<string,string>(StringComparer.Ordinal);
        private static bool active;
        private static int generation;
        private static readonly Dictionary<string,string> failedPayloads=new Dictionary<string,string>(StringComparer.Ordinal);
        private static readonly Dictionary<string,int> failedGenerations=new Dictionary<string,int>(StringComparer.Ordinal);
        private static readonly Dictionary<string,LoadedMod> failedProviders=new Dictionary<string,LoadedMod>(StringComparer.Ordinal);
        internal static void Start()
        {
            if(active)return;active=true;
            ModHook.OnModAssetsLoaded+=AssetsLoaded;
            Refresh();
        }
        internal static void Stop()
        {
            if(!active)return;active=false;ModHook.OnModAssetsLoaded-=AssetsLoaded;
            for(int pass=0;pass<ContentRegistry.MaxPacks && bindings.Count>0;pass++)
            {
                int before=bindings.Count;foreach(var id in bindings.Keys.ToArray())Remove(id);
                if(before==bindings.Count){Debug.LogWarning("[RaidPrototype] Some content still has external dependants; restart is required to fully unload.");break;}
            }
            failures.Clear();failedPayloads.Clear();failedGenerations.Clear();failedProviders.Clear();
        }
        private static void AssetsLoaded(LoadedMod mod){if(active)Refresh();}
        private static bool TryPayload(LoadedMod mod,out string json)
        {
            json=null;
            return mod?.Manifest!=null && mod.Manifest.ID!=FrameworkId && mod.IsEnabled && mod.Localization!=null &&
                mod.Localization.TryGetValue("en",out var table) && table!=null && table.TryGetValue(PayloadKey,out json);
        }
        private static void Remove(string id)
        {
            if(!bindings.TryGetValue(id,out var binding))return;
            try{ContentRegistry.Unregister(binding.PackId);bindings.Remove(id);unchecked{generation++;}Debug.Log("[RaidPrototype] Removed content provider "+id);}
            catch(InvalidOperationException){/* Registered dependants are removed first on the next pass. */}
        }
        internal static void Refresh()
        {
            if(!active || ModLoader.Mods==null)return;
            // Retire disabled/replaced providers and their dependent closure. Existing sessions keep snapshots.
            var retire=new HashSet<string>(StringComparer.Ordinal);
            foreach(var pair in bindings)
            {
                var provider=ModLoader.Mods.FirstOrDefault(m=>m?.Manifest?.ID==pair.Key);
                if(!TryPayload(provider,out var payload) || !ReferenceEquals(provider,pair.Value.Provider) || payload!=pair.Value.Payload)retire.Add(pair.Key);
            }
            bool expanded;
            do
            {
                expanded=false;
                foreach(var pack in ContentRegistry.Packs)
                    if(bindings.ContainsKey(pack.Id) && !retire.Contains(pack.Id) && pack.Requires.Any(retire.Contains)){retire.Add(pack.Id);expanded=true;}
            }while(expanded);
            for(int pass=0;pass<ContentRegistry.MaxPacks;pass++)
            {
                int before=bindings.Count;
                foreach(var id in bindings.Keys.ToArray())
                {
                    if(retire.Contains(id))Remove(id);
                }
                if(before==bindings.Count)break;
            }
            var pending=ModLoader.Mods.Where(m=>TryPayload(m,out _)).ToArray();
            // Content dependencies can register even when the host asset order differs from the pack order.
            for(int pass=0;pass<pending.Length;pass++)
            {
                bool progress=false;
                foreach(var provider in pending)
                {
                    string id=provider.Manifest.ID;if(bindings.ContainsKey(id))continue;
                    TryPayload(provider,out var json);
                    if(failedPayloads.TryGetValue(id,out var failedJson) && failedJson==json && failedGenerations.TryGetValue(id,out var failedAt) && failedAt==generation && failedProviders.TryGetValue(id,out var failedProvider) && ReferenceEquals(failedProvider,provider))continue;
                    try
                    {
                        string registered=ContentLoader.RegisterJsonForProvider(json,id);
                        bindings.Add(id,new Binding{Provider=provider,PackId=registered,Payload=json});
                        failures.Remove(id);failedPayloads.Remove(id);failedGenerations.Remove(id);failedProviders.Remove(id);unchecked{generation++;}progress=true;
                        var loaded=ContentRegistry.Packs.First(p=>p.Id==registered);
                        Debug.Log("[RaidPrototype] Registered CSV content provider "+id+" using API v1; maps="+loaded.Maps.Length+", weapons="+loaded.Weapons.Length+", items="+loaded.Items.Length+", skins="+loaded.Skins.Length+"; mapIds="+string.Join(",",loaded.Maps.Select(m=>m.Id)));
                    }
                    catch(Exception ex)
                    {
                        failedPayloads[id]=json;failedGenerations[id]=generation;failedProviders[id]=provider;
                        string message=ex.Message;
                        if(!failures.TryGetValue(id,out var old) || old!=message){failures[id]=message;Debug.LogWarning("[RaidPrototype] Rejected content provider "+id+": "+message);}
                    }
                }
                if(!progress)break;
            }
        }
    }
}
