using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RaidPrototype.Content;

namespace RaidPrototype
{
    /// <summary>Optional sprite adapter. Core content and simulation have no Unity dependency.</summary>
    internal static class VisualBridge
    {
        private static readonly HashSet<string> warned = new HashSet<string>(StringComparer.Ordinal);
        internal static Color32 Color(RGBA c) => new Color32((byte)Mathf.RoundToInt(c.R*255), (byte)Mathf.RoundToInt(c.G*255), (byte)Mathf.RoundToInt(c.B*255), (byte)Mathf.RoundToInt(c.A*255));
        internal static Sprite Find(string key)
        {
            if(string.IsNullOrWhiteSpace(key))return null;
            int split=key.IndexOf(':');
            if(split>0)
            {
                string id=key.Substring(0,split),name=key.Substring(split+1);
                foreach(var mod in ModLoader.Mods)
                    if(mod!=null && mod.IsEnabled && mod.Manifest!=null && mod.Manifest.ID==id && mod.Sprites!=null && mod.Sprites.TryGetValue(name,out var sprite) && sprite!=null)return sprite;
            }
            if(warned.Add(key))Debug.LogWarning("[RaidPrototype] Missing sprite '"+key+"'; using simple geometry. Place PNG in the provider mod's Textures/Items folder.");
            return null;
        }
    }
    /// <summary>Draws one optional provider sprite on its own UI texture/material, using the same world mapping.</summary>
    public sealed class RaidSpriteView : MonoBehaviour
    {
        private Image image;
        internal string Key;
        internal void Init(Transform parent,string key,Color32 tint)
        {
            transform.SetParent(parent,false);image=gameObject.GetComponent<Image>();Key=key;
            image.sprite=VisualBridge.Find(key);image.raycastTarget=false;image.preserveAspect=false;image.color=tint;
        }
        internal void Place(RaidGraphic field, Vec2 p, Vector2 size, bool visible)
        {
            if(image==null)return;
            if(image.sprite==null)image.sprite=VisualBridge.Find(Key);
            image.enabled=visible && image.sprite!=null;
            var r=image.rectTransform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.pivot=new Vector2(.5f,.5f);
            r.anchoredPosition=field.WorldLocal(p);r.sizeDelta=field.WorldSize(size);
        }
    }
}
