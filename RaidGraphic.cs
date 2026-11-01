using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using RaidPrototype.Content;
using NVec = RaidPrototype.Vec2;

namespace RaidPrototype
{
    // Geometry fallback plus optional content sprites; no real inventory or save objects.
    public sealed class RaidGraphic : MaskableGraphic
    {
        public RaidModel Model;
        public Vector2 Aim;
        public float ShotFlash;
        private float scale;
        private Vector2 origin;
        private RaidModel renderedModel;
        private bool spritesBuilt;
        private Enemy firstEnemy;
        private LootBox firstBox;
        private MapDefinition mapData;
        private SkinDefinition skinData;
        private GameObject backgroundObject;
        private Image backgroundImage;
        private RaidForeground foreground;
        private RectTransform backgroundRect;
        private readonly List<RaidSpriteView> views=new List<RaidSpriteView>();
        private void Cache()
        {
            bool sameEntities=Model==null || ((Model.Enemies.Count==0?null:Model.Enemies[0])==firstEnemy && (Model.Boxes.Count==0?null:Model.Boxes[0])==firstBox);
            if(ReferenceEquals(renderedModel,Model) && sameEntities)return;
            ClearSprites();spritesBuilt=false;renderedModel=Model;mapData=Model?.Map;skinData=Model?.Skin;
            firstEnemy=Model!=null && Model.Enemies.Count>0?Model.Enemies[0]:null;firstBox=Model!=null && Model.Boxes.Count>0?Model.Boxes[0]:null;
        }
        private void Mapping()
        {
            Cache();var r=rectTransform.rect;
            float w=Model?.MapBounds.MaxX ?? 20, h=Model?.MapBounds.MaxY ?? 14;
            scale=Mathf.Min(r.width/w,r.height/h);origin=r.center-new Vector2(w/2,h/2)*scale;
        }
        private Vector2 P(NVec p)=>origin+new Vector2(p.X,p.Y)*scale;
        internal Vector2 WorldLocal(NVec p){Mapping();return P(p);}
        internal Vector2 WorldSize(Vector2 size){Mapping();return size*scale;}
        public bool MouseWorld(Vector2 screen,out NVec world)
        {
            Mapping();RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform,screen,null,out var local);
            var p=(local-origin)/Mathf.Max(.001f,scale);world=new NVec(p.x,p.y);
            return p.x>=0&&p.x<=(Model?.MapBounds.MaxX??20)&&p.y>=0&&p.y<=(Model?.MapBounds.MaxY??14);
        }
        public void Refresh(){SetVerticesDirty();UpdateSprites();if(foreground!=null)foreground.SetVerticesDirty();}
        private RaidSpriteView AddSprite(string key,RGBA tint)
        {
            if(string.IsNullOrEmpty(key))return null;
            var go=new GameObject("ContentSprite",typeof(RectTransform),typeof(Image),typeof(RaidSpriteView));
            var v=go.GetComponent<RaidSpriteView>();v.Init(transform,key,VisualBridge.Color(tint));views.Add(v);return v;
        }
        private readonly List<Action> placeSprites=new List<Action>();
        private void ClearSprites()
        {
            foreach(var v in views)if(v!=null){v.gameObject.SetActive(false);UnityEngine.Object.Destroy(v.gameObject);}
            views.Clear();placeSprites.Clear();
            if(foreground!=null){foreground.gameObject.SetActive(false);UnityEngine.Object.Destroy(foreground.gameObject);}foreground=null;
            if(backgroundObject!=null){backgroundObject.SetActive(false);UnityEngine.Object.Destroy(backgroundObject);}
            backgroundObject=null;backgroundImage=null;backgroundRect=null;
        }
        private void UpdateSprites()
        {
            Cache();
            if(Model!=null && !spritesBuilt)
            {
                Cache();spritesBuilt=true;
                if(Model!=null)
                {
                    var m=Model;var skin=skinData;var bounds=m.MapBounds;
                    // The background is a sibling BEFORE this world mesh, never an opaque child above it.
                    if(!string.IsNullOrEmpty(skin.BackgroundKey))
                    {
                        backgroundObject=new GameObject("RaidBackground",typeof(RectTransform),typeof(Image));
                        backgroundRect=backgroundObject.GetComponent<RectTransform>();backgroundRect.SetParent(rectTransform.parent,false);
                        backgroundRect.SetSiblingIndex(rectTransform.GetSiblingIndex());
                        backgroundImage=backgroundObject.GetComponent<Image>();backgroundImage.sprite=VisualBridge.Find(skin.BackgroundKey);backgroundImage.raycastTarget=false;
                    }
                    foreach(var wall in m.Obstacles){var r=wall;var v=AddSprite(skin.WallKey,skin.Wall);if(v!=null)placeSprites.Add(()=>v.Place(this,new NVec((r.MinX+r.MaxX)/2,(r.MinY+r.MaxY)/2),new Vector2(r.MaxX-r.MinX,r.MaxY-r.MinY),true));}
                    foreach(var box in m.Boxes){var b=box;var v=AddSprite(string.IsNullOrEmpty(b.SpriteKey)?skin.CrateKey:b.SpriteKey,skin.Crate);if(v!=null)placeSprites.Add(()=>v.Place(this,b.Position,new Vector2(.7f,.7f),!b.Opened));}
                    foreach(var enemy in m.Enemies){var e=enemy;var v=AddSprite(string.IsNullOrEmpty(e.SpriteKey)?skin.EnemyKey:e.SpriteKey,skin.Enemy);if(v!=null)placeSprites.Add(()=>v.Place(this,e.Position,new Vector2(RaidModel.EnemyRadius*2,RaidModel.EnemyRadius*2),e.Alive));}
                    var player=AddSprite(skin.PlayerKey,skin.Player);if(player!=null)placeSprites.Add(()=>player.Place(this,m.PlayerPosition,new Vector2(RaidModel.PlayerRadius*2,RaidModel.PlayerRadius*2),true));
                    var fg=new GameObject("RaidForeground",typeof(RectTransform),typeof(CanvasRenderer),typeof(RaidForeground));fg.transform.SetParent(transform,false);
                    foreground=fg.GetComponent<RaidForeground>();foreground.Owner=this;foreground.raycastTarget=false;
                    var fr=foreground.rectTransform;fr.anchorMin=Vector2.zero;fr.anchorMax=Vector2.one;fr.offsetMin=Vector2.zero;fr.offsetMax=Vector2.zero;
                }
            }
            if(backgroundRect!=null && Model!=null)
            {
                Mapping();backgroundRect.anchorMin=rectTransform.anchorMin;backgroundRect.anchorMax=rectTransform.anchorMax;
                float padX=(rectTransform.rect.width-Model.MapBounds.MaxX*scale)/2f,padY=(rectTransform.rect.height-Model.MapBounds.MaxY*scale)/2f;
                backgroundRect.offsetMin=rectTransform.offsetMin+new Vector2(padX,padY);backgroundRect.offsetMax=rectTransform.offsetMax-new Vector2(padX,padY);
                backgroundRect.pivot=rectTransform.pivot;
                if(backgroundImage.sprite==null)backgroundImage.sprite=VisualBridge.Find(skinData.BackgroundKey);
                backgroundImage.enabled=backgroundImage.sprite!=null;
            }
            foreach(var place in placeSprites)place();
        }
        protected override void OnDestroy(){ClearSprites();base.OnDestroy();}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();if(Model==null)return;Mapping();var s=skinData;float w=Model.MapBounds.MaxX,h=Model.MapBounds.MaxY;
            if(backgroundImage==null || backgroundImage.sprite==null)Quad(vh,origin,new Vector2(w,h)*scale,VisualBridge.Color(s.Floor));
            int step=Math.Max(1,(int)Math.Ceiling(Math.Max(w,h)/100f));
            for(int x=0;x<=w;x+=step)Line(vh,origin+new Vector2(x,0)*scale,origin+new Vector2(x,h)*scale,1,VisualBridge.Color(s.Grid));
            for(int y=0;y<=h;y+=step)Line(vh,origin+new Vector2(0,y)*scale,origin+new Vector2(w,y)*scale,1,VisualBridge.Color(s.Grid));
            foreach(var wall in Model.Obstacles){Quad(vh,P(new NVec(wall.MinX,wall.MinY)),new Vector2(wall.MaxX-wall.MinX,wall.MaxY-wall.MinY)*scale,VisualBridge.Color(s.Wall));Line(vh,P(new NVec(wall.MinX,wall.MaxY)),P(new NVec(wall.MaxX,wall.MaxY)),3,VisualBridge.Color(s.WallEdge));}
            if(mapData.HasExtraction)
            {
                var exit=P(Model.ExtractionPoint);float r=mapData.ExtractionRadius;
                Circle(vh,exit,r*scale,VisualBridge.Color(s.Exit));Ring(vh,exit,r*scale,2,VisualBridge.Color(s.ExitEdge));
                if(Model.ExtractionProgress>0)Circle(vh,exit,r*.8f*(Model.ExtractionProgress/mapData.ExtractionSeconds)*scale,VisualBridge.Color(s.ExitEdge));
            }
            foreach(var box in Model.Boxes){var p=P(box.Position);float size=.65f*scale;Quad(vh,p-Vector2.one*size/2,Vector2.one*size,VisualBridge.Color(box.Opened?s.OpenCrate:s.Crate));Line(vh,p+new Vector2(-size/2,0),p+new Vector2(size/2,0),2,VisualBridge.Color(s.Grid));}
            foreach(var e in Model.Enemies){var p=P(e.Position);if(!e.Alive){Line(vh,p-Vector2.one*6,p+Vector2.one*6,2,VisualBridge.Color(s.OpenCrate));continue;}Circle(vh,p,RaidModel.EnemyRadius*scale,VisualBridge.Color(s.Enemy));Quad(vh,p+new Vector2(-12,18),new Vector2(24*e.Health/e.MaxHealth,3),VisualBridge.Color(s.Enemy));}
            Circle(vh,P(Model.PlayerPosition),RaidModel.PlayerRadius*scale,VisualBridge.Color(s.Player));
            if(foreground==null)PopulateForeground(vh);
        }
        internal void PopulateForeground(VertexHelper vh)
        {
            if(Model==null)return;Mapping();var s=skinData;var playerPos=P(Model.PlayerPosition);
            var aim=P(new NVec(Aim.x,Aim.y))-playerPos;if(aim.sqrMagnitude>.001f)Line(vh,playerPos,playerPos+aim.normalized*.7f*scale,5,VisualBridge.Color(s.UIText));
            if(ShotFlash>0)Line(vh,P(Model.LastShotStart),P(Model.LastShotEnd),2,VisualBridge.Color(s.Bullet));
            var cursor=P(new NVec(Aim.x,Aim.y));Line(vh,cursor-new Vector2(6,0),cursor+new Vector2(6,0),1,VisualBridge.Color(s.UIText));Line(vh,cursor-new Vector2(0,6),cursor+new Vector2(0,6),1,VisualBridge.Color(s.UIText));
            foreach(var enemy in Model.Enemies)if(enemy.Alive){var p=P(enemy.Position);Quad(vh,p+new Vector2(-12,18),new Vector2(24*enemy.Health/enemy.MaxHealth,3),VisualBridge.Color(s.Enemy));}
        }
        private static void Quad(VertexHelper v,Vector2 p,Vector2 size,Color32 c){int i=v.currentVertCount;v.AddVert(p,c,Vector2.zero);v.AddVert(p+new Vector2(0,size.y),c,Vector2.zero);v.AddVert(p+size,c,Vector2.zero);v.AddVert(p+new Vector2(size.x,0),c,Vector2.zero);v.AddTriangle(i,i+1,i+2);v.AddTriangle(i,i+2,i+3);}
        private static void Line(VertexHelper v,Vector2 a,Vector2 b,float thickness,Color32 c){var d=b-a;if(d.sqrMagnitude<.00001f)return;var n=new Vector2(-d.y,d.x).normalized*thickness/2;int i=v.currentVertCount;v.AddVert(a-n,c,Vector2.zero);v.AddVert(a+n,c,Vector2.zero);v.AddVert(b+n,c,Vector2.zero);v.AddVert(b-n,c,Vector2.zero);v.AddTriangle(i,i+1,i+2);v.AddTriangle(i,i+2,i+3);}
        private static void Circle(VertexHelper v,Vector2 p,float r,Color32 c){int i=v.currentVertCount;v.AddVert(p,c,Vector2.zero);const int steps=24;for(int j=0;j<=steps;j++){float a=j*Mathf.PI*2/steps;v.AddVert(p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,c,Vector2.zero);if(j>0)v.AddTriangle(i,i+j,i+j+1);}}
        private static void Ring(VertexHelper v,Vector2 p,float r,float width,Color32 c){for(int i=0;i<40;i++){float a=i*Mathf.PI/20,b=(i+1)*Mathf.PI/20;Line(v,p+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*r,p+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*r,width,c);}}
    }
    public sealed class RaidForeground : MaskableGraphic
    {
        internal RaidGraphic Owner;
        protected override void OnPopulateMesh(VertexHelper vh){vh.Clear();if(Owner!=null)Owner.PopulateForeground(vh);}
    }
}
