using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Blocks.Gameplay.Shooter
{
    /// <summary>Styles the Shooter browser without replacing its session bindings or callbacks.</summary>
    internal static class ShooterSessionBrowserSkin
    {
        static void Place(VisualElement e, float x, float y, float w, float h)
        {
            if (e == null) return;
            e.style.position = Position.Absolute;
            e.style.left = x; e.style.top = y; e.style.width = w; e.style.height = h;
            e.style.minWidth = 0; e.style.minHeight = 0;
            e.style.maxWidth = StyleKeyword.None; e.style.maxHeight = StyleKeyword.None;
            e.style.marginLeft = e.style.marginRight = e.style.marginTop = e.style.marginBottom = 0;
            e.style.flexShrink = 0;
        }
        static VisualElement Find(VisualElement e, string type) => e.Query<VisualElement>().ToList().FirstOrDefault(v => v.GetType().Name == type);
        static Label Text(VisualElement parent, string text, float x, float y, float w, float h, string css = null)
        {
            var label = new Label(text); parent.Add(label); Place(label,x,y,w,h);
            label.style.whiteSpace = WhiteSpace.Normal;
            if (css != null) label.AddToClassList(css);
            return label;
        }
        public static void Apply(VisualElement root, VisualElement layout)
        {
            if (layout.ClassListContains("nexus-screen")) return;
            layout.AddToClassList("nexus-screen");
            layout.styleSheets.Add(Resources.Load<StyleSheet>("SessionUI/Browser"));
            Place(layout,0,0,1672,941);
            layout.style.paddingLeft = layout.style.paddingRight = layout.style.paddingTop = layout.style.paddingBottom = 0;
            layout.style.transformOrigin = new TransformOrigin(0,0);
            System.Action resize = () => {
                float scale = Mathf.Min(root.resolvedStyle.width/1672f, root.resolvedStyle.height/941f);
                if (float.IsNaN(scale) || scale <= 0) return;
                layout.style.scale = new Scale(new Vector3(scale,scale,1));
                layout.style.left = (root.resolvedStyle.width-1672*scale)/2;
                layout.style.top = (root.resolvedStyle.height-941*scale)/2;
            };
            root.RegisterCallback<GeometryChangedEvent>(_ => resize());
            layout.schedule.Execute(resize);
            var templates = layout.Children().ToArray();
            if (templates.Length < 2) return;
            Place(templates[0],148,100,775,734); Place(templates[1],964,162,582,618);
            var left = templates[0].Q(className:"blocks-modal");
            var right = templates[1].Q(className:"blocks-modal");
            Place(left,0,0,775,734); Place(right,0,0,582,618);
            foreach(var card in new[]{left,right})
            {
                var trim=new VisualElement { name="nexus-trim", pickingMode=PickingMode.Ignore };
                trim.style.position=Position.Absolute;
                trim.style.left=trim.style.top=8; trim.style.right=trim.style.bottom=8;
                trim.generateVisualContent += ctx => {
                    float w=trim.contentRect.width,h=trim.contentRect.height;
                    var p=ctx.painter2D; p.strokeColor=new Color(.12f,.72f,.94f,.7f); p.lineWidth=1.5f;
                    p.BeginPath(); p.MoveTo(new Vector2(20,0)); p.LineTo(new Vector2(w-20,0));
                    p.LineTo(new Vector2(w,20));p.LineTo(new Vector2(w,h-20));p.LineTo(new Vector2(w-20,h));
                    p.LineTo(new Vector2(20,h));p.LineTo(new Vector2(0,h-20));p.LineTo(new Vector2(0,20));p.ClosePath();p.Stroke();
                };
                card.Add(trim);
            }
            var heading = left.Q<Label>(); heading.text = "세션 참가";
            heading.AddToClassList("nexus-title"); Place(heading,58,22,600,66);
            Text(left,"JOIN SESSION",60,86,600,28,"nexus-subtitle");
            var create = Find(left,"CreateSessionElement");
            Place(create,58,126,670,270);
            var controls = create.Children().First(); Place(controls,0,0,670,182);
            var mode = create.Q<DropdownField>(); mode.label = "모드 선택"; Place(mode,0,0,670,44);
            var name = create.Q<TextField>(); name.label = "세션 이름 입력";
            name.textEdition.placeholder = "세션 이름을 입력하세요..."; Place(name,0,59,670,44);
            name.labelElement.style.display = DisplayStyle.Flex;
            name.style.borderTopWidth = name.style.borderBottomWidth = name.style.borderLeftWidth = name.style.borderRightWidth = 0;
            var createButton = controls.Q<Button>(); createButton.text = "+  세션 생성";
            createButton.AddToClassList("nexus-primary"); Place(createButton,0,125,670,56);
            var recovery = create.Children().OfType<Button>().FirstOrDefault(); Place(recovery,0,194,670,54);
            var status = create.Children().OfType<Label>().FirstOrDefault(); Place(status,0,251,670,25);
            if(status != null) status.style.fontSize = 16;
            Text(left,"세션 목록",58,396,440,35);
            var count = Text(left,"0개의 세션",550,396,178,35); count.style.unityTextAlign = TextAnchor.MiddleRight;
            var browser = Find(left,"SessionBrowserElement") as ListView;
            Place(browser,52,435,676,262);
            browser.style.flexGrow = 0;
            browser.fixedItemHeight = 52;
            var scroll = browser.Q<ScrollView>(); Place(scroll,0,0,676,192);
            browser.schedule.Execute(() => {
                count.text = (browser.itemsSource == null ? 0 : browser.itemsSource.Count) + "개의 세션";
                foreach(var button in browser.Query<Button>().ToList())
                {
                    if(button.text == "JOIN" || button.text == "참가") {button.text="참가"; Place(button,0,0,326,58);}
                    else if(button.text == "REFRESH LIST" || button.text == "목록 새로고침") {button.text="목록 새로고침"; Place(button,346,0,326,58);}
                    else continue;
                    Place(button.parent,0,204,676,58);
                    Place(button.parent.parent,0,0,676,262);
                }
                foreach(var label in browser.Query<Label>().ToList())
                    if(label.text == "No sessions found") {
                        label.text="참가 가능한 세션이 없습니다\n\n다른 플레이어가 생성한 세션을 기다리거나\n직접 세션을 만들어 보세요.";
                        label.style.whiteSpace=WhiteSpace.Normal; label.style.unityTextAlign=TextAnchor.MiddleCenter;
                        label.style.height=184; label.style.fontSize=20;
                    }
            }).Every(250);

            // Keep bound controls in their original hierarchy; flatten only their layout.
            foreach(var row in right.Children().Where(v=>v.GetType()==typeof(VisualElement) && v.name!="nexus-trim").ToArray()) Place(row,0,0,582,618);
            Text(right,"현재 세션",42,22,495,62,"nexus-title");
            var sessionName = Find(right,"SessionNameLabel"); Place(sessionName,42,86,495,28);
            if(sessionName!=null)sessionName.style.fontSize=18;
            Text(right,"참가 인원",42,115,300,42);
            var players = Find(right,"PlayerCountLabel"); Place(players,350,115,190,42);
            Place(Find(right,"PlayerNameLabel"),42,165,495,40);
            Place(Find(right,"CopySessionCodeElement"),42,215,495,40);
            var codeControl=Find(right,"CopySessionCodeElement");
            if(codeControl!=null) {
                Place(codeControl.Q<TextField>(),0,0,380,40);
                var copy=codeControl.Q<Button>(); Place(copy,395,0,100,40);
                if(copy!=null)copy.text="복사";
            }
            var playerList=Find(right,"PlayerListViewElement") as ListView;
            Place(playerList,38,266,506,210);
            var leave=Find(right,"LeaveSessionButton") as Button; Place(leave,38,496,506,58);
            if(leave!=null)leave.text="세션 나가기";
            Place(Find(right,"SessionStatusLabel"),42,568,495,30);
            right.schedule.Execute(() => {
                foreach(var label in right.Query<Label>().ToList()) {
                    switch(label.text) {
                        case "No Session joined": label.text="MULTIPLAYER · READY FOR BATTLE"; break;
                        case "- / - Players": label.text="0 / 4"; break;
                        case "No Player Joined": label.text="참가한 플레이어 없음"; break;
                        case "Not in a session": label.text="세션 미참가 상태"; break;
                        case "No player has joined":
                            label.text="현재 참가 중인 세션이 없습니다\n\n왼쪽에서 세션을 생성하거나\n목록에서 세션에 참가하세요.";
                            label.style.whiteSpace=WhiteSpace.Normal; label.style.unityTextAlign=TextAnchor.MiddleCenter;
                            label.style.height=200; label.style.fontSize=20; break;
                    }
                }
                var code=Find(right,"CopySessionCodeElement");
                if(code!=null) {
                    var field=code.Q<TextField>();
                    if(field!=null && field.value=="No Session Code")field.SetValueWithoutNotify("세션 코드 없음");
                }
            }).Every(250);
        }
    }
}
