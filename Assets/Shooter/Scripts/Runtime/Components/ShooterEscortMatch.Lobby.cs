using System.Linq;
using Unity.Netcode;
using UnityEngine;
using Blocks.Gameplay.Core;
using Unity.Services.Multiplayer;

namespace Blocks.Gameplay.Shooter
{
    public sealed partial class ShooterEscortMatch
    {
        bool escapeMenuOpen, settingsMenuOpen;
        AudioSource lobbyMusic;
        ResourceRequest lobbyMusicRequest;
        bool lobbyMusicLoaded;
        AudioSource matchMusic;
        ResourceRequest matchMusicRequest;
        bool matchMusicLoaded;
        const float MatchMusicVolume = .10f;
        [System.Serializable] class MapMusicTrack { public float start, end; }
        [System.Serializable] class MapMusicTracks { public MapMusicTrack[] tracks; }
        MapMusicTrack[] mapMusicTracks;
        int mapMusicEpoch = -1, mapMusicIndex = -1;
        float mapMusicStart, mapMusicEnd;

        void SelectMapMusic()
        {
            if (mapMusicEpoch == epoch.Value) return;
            if (mapMusicTracks == null)
            {
                var data = Resources.Load<TextAsset>("Audio/MapPlaylistTracks");
                mapMusicTracks = data != null ? JsonUtility.FromJson<MapMusicTracks>(data.text).tracks : new MapMusicTrack[0];
            }
            int count = mapMusicTracks.Length;
            int next = count > 1 && mapMusicIndex >= 0 ? (mapMusicIndex + Random.Range(1,count)) % count : (count > 0 ? Random.Range(0,count) : -1);
            mapMusicIndex = next; mapMusicEpoch = epoch.Value;
            mapMusicStart = next >= 0 ? mapMusicTracks[next].start : 0;
            mapMusicEnd = next >= 0 ? Mathf.Min(mapMusicTracks[next].end,normalMatchClip.length) : normalMatchClip.length;
            matchMusic.Stop();
            currentFinalClip = null;
            matchMusic.clip = normalMatchClip;
            matchMusic.loop = false;
            matchMusic.time = mapMusicStart;
        }
        AudioClip preparationClip;
        AudioSource preparationMusic;
        int preparationMusicEpoch = -1;
        bool preparationClipLoaded;
        float PreparationDuration
        {
            get
            {
                if (!preparationClipLoaded)
                {
                    preparationClipLoaded = true;
                    preparationClip = Resources.Load<AudioClip>("Audio/Preparation");
                }
                return preparationClip != null ? Mathf.Max(.1f, preparationClip.length) : 3f;
            }
        }

        void UpdatePreparationMusic()
        {
            // Load during the lobby, before the synchronized countdown begins.
            float duration = PreparationDuration;
            bool waiting = IsSpawned && phase.Value == 1 && !ReturningToSessionBrowser &&
                NetworkManager != null && !NetworkManager.ShutdownInProgress;
            if (!waiting)
            {
                StopPreparationMusic();
                preparationMusicEpoch = -1;
                return;
            }
            if (preparationClip == null || preparationMusicEpoch == epoch.Value) return;
            if (preparationMusic == null)
            {
                var go = new GameObject("Preparation Music");
                go.transform.SetParent(transform, false);
                preparationMusic = go.AddComponent<AudioSource>();
                preparationMusic.playOnAwake = false;
                preparationMusic.loop = false;
                preparationMusic.spatialBlend = 0;
                preparationMusic.volume = .2f;
                preparationMusic.clip = preparationClip;
            }
            preparationMusicEpoch = epoch.Value;
            float elapsed = Mathf.Max(0, duration - (float)(deadline.Value - Now));
            if (elapsed >= preparationClip.length) return;
            preparationMusic.time = elapsed;
            preparationMusic.Play();
        }
        void StopPreparationMusic()
        {
            if (preparationMusic != null && preparationMusic.isPlaying) preparationMusic.Stop();
        }
        ResourceRequest finalWinningRequest, finalLosingRequest;
        AudioClip normalMatchClip;
        AudioClip currentFinalClip;

        bool LocalPlayerLeading()
        {
            ulong local = NetworkManager.LocalClientId;
            if (TeamMode) return TeamOf(local) == 1;
            int mine = -1, best = -1;
            for (int i = 0; i < slots.Count && i < killScores.Count; i++)
            {
                if (slots[i] == Empty) continue;
                best = Mathf.Max(best, killScores[i]);
                if (slots[i] == local) mine = killScores[i];
            }
            return mine >= 0 && mine == best;
        }

        void UpdateMatchMusic()
        {
            bool playing=IsSpawned && phase.Value==2 && !ReturningToSessionBrowser &&
                NetworkManager!=null && !NetworkManager.ShutdownInProgress;
            if(!playing){StopMatchMusic();return;}
            if (finalWinningRequest == null) finalWinningRequest = Resources.LoadAsync<AudioClip>("Audio/Final30Winning");
            if (finalLosingRequest == null) finalLosingRequest = Resources.LoadAsync<AudioClip>("Audio/Final30Losing");
            if(!matchMusicLoaded)
            {
                if(matchMusicRequest==null)matchMusicRequest=Resources.LoadAsync<AudioClip>("Audio/MapPlaylist");
                if(!matchMusicRequest.isDone)return;
                matchMusicLoaded=true;
                var clip=matchMusicRequest.asset as AudioClip;
                if(clip==null){Debug.LogWarning("경기 BGM을 불러오지 못했습니다.");return;}
                var musicObject=new GameObject("Match BGM");
                musicObject.transform.SetParent(transform,false);
                matchMusic=musicObject.AddComponent<AudioSource>();
                matchMusic.playOnAwake=false;
                matchMusic.loop=true;
                matchMusic.spatialBlend=0;
                // Give gameplay effects priority when Unity has to virtualize audio voices.
                matchMusic.priority=200;
                matchMusic.volume=0;
                matchMusic.clip=clip;
                normalMatchClip=clip;
            }
            if(matchMusic==null)return;
            SelectMapMusic();
            double remaining = deadline.Value - Now;
            if (remaining > 0 && remaining <= 30)
            {
                var request = LocalPlayerLeading() ? finalWinningRequest : finalLosingRequest;
                var finalClip = request.isDone ? request.asset as AudioClip : null;
                if (finalClip != null)
                {
                    if (currentFinalClip != finalClip)
                    {
                        matchMusic.Stop();
                        matchMusic.clip = currentFinalClip = finalClip;
                        matchMusic.loop = false;
                        float elapsed = Mathf.Max(0, 30 - (float)remaining);
                        if (elapsed < finalClip.length)
                        {
                            matchMusic.time = elapsed;
                            matchMusic.Play();
                        }
                    }
                    matchMusic.volume = Mathf.MoveTowards(matchMusic.volume, .25f, Time.unscaledDeltaTime * .3f);
                    return;
                }
            }
            if (currentFinalClip != null)
            {
                matchMusic.Stop(); currentFinalClip = null;
                matchMusic.clip = normalMatchClip; matchMusic.loop = false;
                matchMusic.time = mapMusicStart;
            }
            if(!matchMusic.isPlaying || matchMusic.time >= mapMusicEnd)
            {
                matchMusic.Stop(); matchMusic.time=mapMusicStart;
                matchMusic.volume=0; matchMusic.Play();
            }
            matchMusic.volume=Mathf.MoveTowards(matchMusic.volume,MatchMusicVolume,Time.unscaledDeltaTime*.05f);
        }
        void StopMatchMusic()
        {
            if(matchMusic==null)return;
            if(matchMusic.isPlaying)matchMusic.Stop();
            matchMusic.volume=0;
            currentFinalClip=null;
            matchMusic.clip=normalMatchClip;
            matchMusic.loop=false;
        }

        void UpdateLobbyMusic()
        {
            bool waiting=IsSpawned && phase.Value==0 && !ReturningToSessionBrowser &&
                NetworkManager!=null && !NetworkManager.ShutdownInProgress;
            if(!waiting){StopLobbyMusic();return;}
            if(!lobbyMusicLoaded)
            {
                if(lobbyMusicRequest==null)lobbyMusicRequest=Resources.LoadAsync<AudioClip>("Audio/LobbyBGM");
                if(!lobbyMusicRequest.isDone)return;
                lobbyMusicLoaded=true;
                var clip=lobbyMusicRequest.asset as AudioClip;
                if(clip==null){Debug.LogWarning("대기실 BGM을 불러오지 못했습니다.");return;}
                var musicObject=new GameObject("Lobby BGM");
                musicObject.transform.SetParent(transform,false);
                lobbyMusic=musicObject.AddComponent<AudioSource>();
                lobbyMusic.playOnAwake=false;
                lobbyMusic.loop=true;
                lobbyMusic.spatialBlend=0;
                lobbyMusic.volume=.35f;
                lobbyMusic.clip=clip;
            }
            if(lobbyMusic!=null && !lobbyMusic.isPlaying)lobbyMusic.Play();
        }
        void StopLobbyMusic(){if(lobbyMusic!=null && lobbyMusic.isPlaying)lobbyMusic.Stop();}
        void OnDisable(){StopLobbyMusic();StopMatchMusic();StopPreparationMusic();}
        float settingsSound, settingsLook, settingsBrightness;
        UnityEngine.Rendering.Volume settingsVolume;
        UnityEngine.Rendering.VolumeProfile settingsProfile;
        UnityEngine.Rendering.Universal.ColorAdjustments settingsColor;

        void InitializeSettings()
        {
            settingsSound=Mathf.Clamp01(PlayerPrefs.GetFloat("Shooter.Volume",1));
            settingsLook=Mathf.Clamp(PlayerPrefs.GetFloat("Shooter.LookMultiplier",1),.25f,2f);
            settingsBrightness=Mathf.Clamp(PlayerPrefs.GetFloat("Shooter.Exposure",0),-1f,1f);
            var go=new GameObject("LocalDisplaySettings");
            go.transform.SetParent(transform,false);
            settingsVolume=go.AddComponent<UnityEngine.Rendering.Volume>();
            settingsVolume.isGlobal=true;settingsVolume.priority=10000;
            settingsProfile=ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            settingsVolume.sharedProfile=settingsProfile;
            settingsColor=settingsProfile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>();
            ApplyLocalSettings();
        }
        void ApplyLocalSettings()
        {
            AudioListener.volume=settingsSound;
            CoreCameraController.UserLookSensitivityMultiplier=settingsLook;
            if(settingsColor!=null)settingsColor.postExposure.Override(settingsBrightness);
        }
        void SaveLocalSettings()
        {
            PlayerPrefs.SetFloat("Shooter.Volume",settingsSound);
            PlayerPrefs.SetFloat("Shooter.LookMultiplier",settingsLook);
            PlayerPrefs.SetFloat("Shooter.Exposure",settingsBrightness);
            PlayerPrefs.Save();
        }
        void UpdateEscapeMenu()
        {
            // Volume affects only this client's camera, never shared scene lights.
            if(Camera.main!=null && Camera.main.TryGetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(out var data))
                data.renderPostProcessing=true;
            var keyboard=UnityEngine.InputSystem.Keyboard.current;
            if(keyboard==null || !keyboard.escapeKey.wasPressedThisFrame)return;
            if(settingsMenuOpen){settingsMenuOpen=false;SaveLocalSettings();return;}
            escapeMenuOpen=!escapeMenuOpen;
            SaveLocalSettings();
            if(escapeMenuOpen){Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
            if(IsSpawned)UpdateLobbyInput();
        }
        void DrawEscapeMenu()
        {
            GUI.depth=-2000;
            var oldMatrix=GUI.matrix;
            float scale=Mathf.Min(Screen.width/600f,Screen.height/500f);
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale);
            float width=Screen.width/scale,height=Screen.height/scale;
            GUI.Box(new Rect(0,0,width,height),"");
            GUILayout.BeginArea(new Rect((width-420)/2,(height-420)/2,420,420),GUI.skin.box);
            GUILayout.Space(16);
            GUILayout.Label(settingsMenuOpen?"설정":"게임 메뉴",new GUIStyle(GUI.skin.label){fontSize=24,alignment=TextAnchor.MiddleCenter});
            GUILayout.Space(16);
            if(settingsMenuOpen)
            {
                GUI.changed=false;
                GUILayout.Label($"사운드  {settingsSound*100:0}%");
                settingsSound=GUILayout.HorizontalSlider(settingsSound,0,1,GUILayout.Height(24));
                GUILayout.Label($"마우스 감도  {settingsLook:0.00}배");
                settingsLook=GUILayout.HorizontalSlider(settingsLook,.25f,2f,GUILayout.Height(24));
                GUILayout.Label($"밝기  {settingsBrightness:+0.00;-0.00;0.00}");
                settingsBrightness=GUILayout.HorizontalSlider(settingsBrightness,-1,1,GUILayout.Height(24));
                if(GUI.changed)ApplyLocalSettings();
                GUILayout.Space(16);
                if(GUILayout.Button("기본값 복원",GUILayout.Height(36)))
                {settingsSound=1;settingsLook=1;settingsBrightness=0;ApplyLocalSettings();SaveLocalSettings();}
                if(GUILayout.Button("뒤로",GUILayout.Height(40))){settingsMenuOpen=false;SaveLocalSettings();}
            }
            else
            {
                if(GUILayout.Button("계속하기",GUILayout.Height(48)))
                {escapeMenuOpen=false;SaveLocalSettings();if(IsSpawned)UpdateLobbyInput();}
                GUILayout.Space(12);
                if(GUILayout.Button("설정",GUILayout.Height(48)))settingsMenuOpen=true;
                GUILayout.Space(12);
                GUI.enabled=!leavingRoom;
                if(GUILayout.Button("방 나가기",GUILayout.Height(48)))LeaveLobby();
                GUI.enabled=true;
                GUILayout.Label(lobbyMessage);
                GUILayout.Space(12);
                GUILayout.Label("온라인 경기는 메뉴를 열어도 계속 진행됩니다.");
            }
            GUILayout.EndArea();GUI.matrix=oldMatrix;
        }
        // The map is already in this scene. Loading waits for every player's local
        // spawn, camera and reset to finish, rather than loading a second map.
        NetworkList<ulong> readyPlayers;
        NetworkList<ulong> loadedPlayers;
        bool inputBlocked;
        bool sessionBusy;
        bool leavingRoom;
        public bool ReturningToSessionBrowser => leavingRoom || !string.IsNullOrEmpty(exitError);
        string exitError="";
        ISession exitingSession;
        int loadingFrame = -1, acknowledgedEpoch = -1;
        string lobbyMessage = "";
        int ParticipantCount => Enumerable.Range(0, slots.Count).Count(i => slots[i] != Empty);
        bool CanStart => initialized && phase.Value == 0 &&
            (TeamMode ? ParticipantCount >= 1 : ParticipantCount >= 2) &&
            (TeamMode || Enumerable.Range(0, slots.Count).All(i => slots[i] == Empty || readyPlayers.Contains(slots[i])));

        [Rpc(SendTo.Authority, InvokePermission = RpcInvokePermission.Everyone)]
        void SetReadyRpc(bool value, RpcParams rpc = default)
        {
            if (!HasAuthority || phase.Value != 0 || TeamOf(rpc.Receive.SenderClientId) < 0) return;
            ulong id = rpc.Receive.SenderClientId;
            if (value && !readyPlayers.Contains(id)) readyPlayers.Add(id);
            if (!value) readyPlayers.Remove(id);
        }

        int TeamCount(int team) => Enumerable.Range(0, slots.Count).Count(i => i % 2 == team && slots[i] != Empty);

        [Rpc(SendTo.Authority, InvokePermission = RpcInvokePermission.Everyone)]
        void SelectTeamRpc(int team, RpcParams rpc = default)
        {
            if (!HasAuthority || !TeamMode || phase.Value != 0 || sessionBusy || team < 0 || team > 1) return;
            ulong id = rpc.Receive.SenderClientId;
            int source = -1, destination = -1;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] == id) source = i;
                if (i % 2 == team && (slots[i] == Empty || BotInSlot(i)) && destination < 0) destination = i;
            }
            if (source < 0 || source % 2 == team || destination < 0) return;
            readyPlayers.Remove(slots[destination]);
            slots[source] = Empty;
            slots[destination] = id;
            readyPlayers.Remove(id);
            FillBotSlots();
        }

        async void StartLoading()
        {
            if (!HasAuthority || !CanStart || sessionBusy) return;
            sessionBusy = true;
            try
            {
                if (observer?.Session?.IsHost == true)
                {
                    var host = observer.Session.AsHost();
                    host.IsLocked = true;
                    await host.SavePropertiesAsync();
                }
                if (!IsSpawned || !HasAuthority || leavingRoom) return;
                if (!CanStart) { UnlockRoom(); return; }
                loadedPlayers.Clear();
                for(int i=0;i<slots.Count;i++)if(BotInSlot(i))loadedPlayers.Add(slots[i]);
                epoch.Value++;
                deadline.Value = Now + 30;
                phase.Value = 6;
                lobbyMessage = "";
            }
            catch (System.Exception) { lobbyMessage = "방 시작 요청에 실패했습니다. 연결을 확인하고 다시 시도해 주세요."; UnlockRoom(); }
            finally { sessionBusy = false; }
        }

        async void UnlockRoom()
        {
            try
            {
                if (observer?.Session?.IsHost != true) return;
                var host = observer.Session.AsHost();
                host.IsLocked = false;
                await host.SavePropertiesAsync();
            }
            catch (System.Exception) { lobbyMessage = "방 잠금 해제에 실패했습니다. 방을 나간 뒤 다시 만들어 주세요."; }
        }

        async void LeaveLobby()
        {
            if(leavingRoom)return;
            // Reveal the session browser immediately; cloud cleanup must not hold the lobby open.
            exitingSession=exitingSession ?? observer?.Session;
            leavingRoom=true;exitError="";sessionBusy=true;
            StopLobbyMusic();
            StopMatchMusic();
            escapeMenuOpen=false;settingsMenuOpen=false;lobbyMessage="";
            SaveLocalSettings();UpdateLobbyInput();
            FindAnyObjectByType<GameManager>()?.ShowSessionBrowser();
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            try
            {
                if(exitingSession!=null)await exitingSession.LeaveAsync();
                else
                {
                    // Direct/local testing has no service session to own network shutdown.
                    var manager=Unity.Netcode.NetworkManager.Singleton;
                    if(manager!=null && manager.IsListening && !manager.ShutdownInProgress)manager.Shutdown();
                }
                exitingSession=null;
            }
            catch(System.Exception)
            {
                exitError="연결 정리가 완료되지 않았습니다. 다시 시도해 주세요.";
            }
            finally{leavingRoom=false;sessionBusy=false;}
        }

        void DrawRoomExitStatus()
        {
            GUI.depth=-2000;
            float width=Mathf.Min(520,Screen.width-24);
            GUILayout.BeginArea(new Rect((Screen.width-width)/2,12,width,90),GUI.skin.box);
            GUILayout.Label(leavingRoom?"방 목록으로 돌아왔습니다. 이전 연결 정리 중…":exitError);
            if(!leavingRoom && GUILayout.Button("연결 정리 다시 시도",GUILayout.Height(32)))LeaveLobby();
            GUILayout.EndArea();
        }

        [Rpc(SendTo.Authority, InvokePermission = RpcInvokePermission.Everyone)]
        void LoadedRpc(int version, RpcParams rpc = default)
        {
            ulong id = rpc.Receive.SenderClientId;
            if (!HasAuthority || phase.Value != 6 || version != epoch.Value || TeamOf(id) < 0) return;
            if (!loadedPlayers.Contains(id)) loadedPlayers.Add(id);
        }

        void UpdateLoading(WeaponController[] players)
        {
            bool missing = Enumerable.Range(0, slots.Count).Any(i => slots[i] != Empty && !BotInSlot(i) &&
                !players.Any(p => p.OwnerClientId == slots[i]));
            if (missing || Now >= deadline.Value)
            {
                phase.Value = 0;
                readyPlayers.Clear();loadedPlayers.Clear();
                UnlockRoom();
                lobbyMessage = missing ? "참가자가 나갔습니다. 다시 준비해 주세요." : "준비 시간이 초과되었습니다. 다시 준비해 주세요.";
                return;
            }
            if (Enumerable.Range(0, slots.Count).All(i => slots[i] == Empty || loadedPlayers.Contains(slots[i])))
                BeginPreparation();
        }

        void AcknowledgeLoading()
        {
            if (phase.Value != 6) { loadingFrame = -1; return; }
            if (acknowledgedEpoch == epoch.Value || Camera.main == null ||
                localState.GetComponent<WeaponController>() == null) return;
            if (loadingFrame < 0) { loadingFrame = Time.frameCount; return; }
            if (Time.frameCount <= loadingFrame + 1) return;
            acknowledgedEpoch = epoch.Value;
            LoadedRpc(epoch.Value);
        }

        void UpdateLobbyInput()
        {
            bool blocked = leavingRoom || !string.IsNullOrEmpty(exitError) || escapeMenuOpen || !CombatAllowed || (localState != null && (!localState.IsActive || TeamOf(localState.OwnerClientId) < 0));
            if (localState != null && (blocked || inputBlocked))
            {
                localState.GetComponent<CoreInputHandler>()?.SetGameplayInputEnabled(!blocked && localState.IsActive);
                var shooterInput = localState.GetComponent<ShooterInputHandler>();
                if (shooterInput != null) shooterInput.enabled = !blocked && localState.IsActive;
                if (blocked) localState.GetComponent<CoreMovement>()?.ResetMovementForces();
            }
            if (blocked)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (inputBlocked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            inputBlocked = blocked;
        }

        void ReleaseLobbyInput()
        {
            if (localState != null)
            {
                bool enable=localState.IsSpawned && !leavingRoom && NetworkManager!=null && NetworkManager.IsListening && !NetworkManager.ShutdownInProgress;
                localState.GetComponent<CoreInputHandler>()?.SetGameplayInputEnabled(enable);
                var input = localState.GetComponent<ShooterInputHandler>();
                if (input != null) input.enabled = enable;
            }
            inputBlocked = false; loadingFrame = -1; acknowledgedEpoch = -1; sessionBusy = false;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }

        public bool CanSelectLobbyCharacter => IsSpawned && phase.Value == 0 && !sessionBusy;
        NetworkList<Unity.Collections.FixedString512Bytes> lobbyChat;
        readonly System.Collections.Generic.Dictionary<ulong,double> chatLastSent = new System.Collections.Generic.Dictionary<ulong,double>();
        string chatDraft = "";
        Vector2 chatScroll;
        int chatSeenCount;
        Texture2D lobbyBackground, lobbyArtwork;
        bool lobbyArtLoaded;
        Texture2D[] loadingImages;
        void DrawLoadingScreen()
        {
            if(loadingImages==null)
            {
                loadingImages=new Texture2D[4];
                for(int i=0;i<4;i++)loadingImages[i]=Resources.Load<Texture2D>("Loading/Nexus"+(i+1));
            }
            float countdown=phase.Value==1?Mathf.Clamp01(1-(float)(deadline.Value-Now)/PreparationDuration):0;
            // First stage is actual player readiness, followed by the shared start countdown.
            float progress=phase.Value==6?.25f*Mathf.Clamp01(loadedPlayers.Count/(float)Mathf.Max(1,ParticipantCount)):
                Mathf.Lerp(.25f,1,Mathf.Clamp01(countdown/.95f));
            int imageIndex=phase.Value==6?0:Mathf.Min(3,1+Mathf.FloorToInt(countdown*3));
            GUI.depth=-1000;
            var matrix=GUI.matrix;
            LobbyFill(new Rect(0,0,Screen.width,Screen.height),Color.black);
            float scale=Mathf.Min(Screen.width/835f,Screen.height/470f);
            GUI.matrix=Matrix4x4.TRS(new Vector3((Screen.width-835*scale)*.5f,(Screen.height-470*scale)*.5f,0),Quaternion.identity,new Vector3(scale,scale,1));
            if(loadingImages[imageIndex]!=null)GUI.DrawTexture(new Rect(0,0,835,470),loadingImages[imageIndex],ScaleMode.StretchToFill);
            // Replace the baked sample percentage and bar with live UI, consistently across all four images.
            LobbyFill(new Rect(31,392,472,59),new Color(.025f,.035f,.05f));
            GUI.Label(new Rect(46,397,390,23),phase.Value==6?$"LOADING...  {loadedPlayers.Count} / {ParticipantCount}":"LOADING...",LobbyText(13,new Color(.88f,.94f,1)));
            var track=new Rect(46,425,392,7);
            LobbyFill(track,new Color(.06f,.085f,.12f));
            LobbyBorder(track,new Color(.32f,.38f,.45f),1);
            float fill=390*progress;
            if(fill>0)
            {
                LobbyFill(new Rect(45,422,fill+4,13),new Color(0,.55f,1,.13f));
                LobbyFill(new Rect(46,424,fill+2,9),new Color(.05f,.65f,1,.32f));
                LobbyFill(new Rect(47,426,fill,5),new Color(.22f,.78f,1));
                LobbyFill(new Rect(47,427,fill,2),new Color(.84f,.97f,1));
            }
            GUI.Label(new Rect(449,417,50,23),Mathf.FloorToInt(progress*100)+"%",LobbyText(14,Color.white));
            if(phase.Value==1)GUI.Label(new Rect(46,441,390,20),$"{Mathf.CeilToInt(Mathf.Max(0,(float)(deadline.Value-Now)))}초 후 경기 시작",LobbyText(11,new Color(.72f,.82f,.92f)));
            GUI.matrix=matrix;
        }
        static readonly Rect[] LobbyPortraits = {
            new Rect(584,598,215,85), new Rect(820,598,177,85),
            new Rect(1057,598,216,85), new Rect(1295,598,216,85)
        };
        void LoadLobbyArt()
        {
            if (lobbyArtLoaded) return;
            lobbyArtLoaded=true;
            lobbyBackground=Resources.Load<Texture2D>("Lobby/HangarBackground");
            lobbyArtwork=Resources.Load<Texture2D>("Lobby/LobbyArtwork");
        }
        void LobbyArtwork(Rect target, Rect source)
        {
            if(lobbyArtwork==null)return;
            // Pixel regions in the approved 1672 x 941 design; no baked labels are drawn.
            GUI.DrawTextureWithTexCoords(target,lobbyArtwork,new Rect(source.x/1672f,1-source.yMax/941f,source.width/1672f,source.height/941f));
        }
        static void LobbyBorder(Rect rect, Color color, float thickness=1)
        {
            LobbyFill(new Rect(rect.x,rect.y,rect.width,thickness),color);
            LobbyFill(new Rect(rect.x,rect.yMax-thickness,rect.width,thickness),color);
            LobbyFill(new Rect(rect.x,rect.y,thickness,rect.height),color);
            LobbyFill(new Rect(rect.xMax-thickness,rect.y,thickness,rect.height),color);
        }
        static void LobbyPanel(Rect rect)
        {
            LobbyFill(rect,new Color(.035f,.065f,.11f,.91f));
            LobbyBorder(rect,new Color(.47f,.60f,.73f,.55f));
        }
        static readonly string[] LobbyHeroes = { "솔저", "겐지", "디바", "캐서디" };
        static readonly string[] LobbyHeroDetails = { "중거리 · 지속 화력", "기동 · 수리검 · 검격", "근거리 · 방어 · 탈출", "정밀 사격 · 순간 화력" };
        static readonly Color LobbyBlue = new Color(.10f,.68f,1f);
        static readonly Color LobbyRed = new Color(1f,.27f,.38f);

        [Rpc(SendTo.Authority, InvokePermission = RpcInvokePermission.Everyone)]
        void SendLobbyChatRpc(string message, RpcParams rpc = default)
        {
            ulong sender = rpc.Receive.SenderClientId;
            if (!HasAuthority || phase.Value != 0 || TeamOf(sender) < 0 || string.IsNullOrWhiteSpace(message)) return;
            if (chatLastSent.TryGetValue(sender,out double last) && Now-last < .7) return;
            message = new string(message.Where(c => !char.IsControl(c)).Take(100).ToArray()).Trim();
            if (message.Length == 0) return;
            var player = WeaponController.ActivePlayers.FirstOrDefault(p => p != null && p.IsSpawned && p.OwnerClientId == sender);
            string name = player != null ? player.GetComponent<CorePlayerState>()?.PlayerName : "플레이어";
            name = string.IsNullOrWhiteSpace(name) ? "플레이어" : new string(name.Where(c => !char.IsControl(c)).Take(16).ToArray());
            string line = name + ": " + message;
            while (System.Text.Encoding.UTF8.GetByteCount(line) > 500) line = line.Substring(0,line.Length-1);
            chatLastSent[sender] = Now;
            if (lobbyChat.Count >= 30) lobbyChat.RemoveAt(0);
            lobbyChat.Add(new Unity.Collections.FixedString512Bytes(line));
        }

        static void LobbyFill(Rect rect, Color color)
        {
            var previous = GUI.color; GUI.color = color;
            GUI.DrawTexture(rect,Texture2D.whiteTexture); GUI.color = previous;
        }
        static GUIStyle LobbyText(int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var style = new GUIStyle(GUI.skin.label) {fontSize=size,alignment=alignment,wordWrap=true,richText=false};
            style.normal.textColor=color; return style;
        }
        static bool LobbyButton(Rect rect, string text, Color color, bool enabled = true)
        {
            bool previous = GUI.enabled; GUI.enabled = previous && enabled;
            bool hover = rect.Contains(Event.current.mousePosition) && GUI.enabled;
            LobbyFill(rect, GUI.enabled ? Color.Lerp(color,Color.white,hover?.12f:0) : new Color(.18f,.22f,.29f));
            LobbyBorder(rect,GUI.enabled?Color.Lerp(color,Color.white,.35f):new Color(.35f,.41f,.49f),1);
            bool pressed = GUI.Button(rect,text,new GUIStyle(LobbyText(18,GUI.enabled?Color.white:new Color(.5f,.55f,.62f),TextAnchor.MiddleCenter)) {fontStyle=FontStyle.Bold});
            GUI.enabled=previous; return pressed;
        }
        void DrawLobbyPlayer(Rect rect, int index, Color accent)
        {
            ulong id = index < slots.Count ? slots[index] : Empty;
            LobbyFill(rect,id==Empty?new Color(.065f,.10f,.17f):Color.Lerp(new Color(.10f,.14f,.22f),accent,.26f));
            LobbyBorder(rect,new Color(.4f,.5f,.6f,.45f));
            LobbyFill(new Rect(rect.xMax-4,rect.y,4,rect.height),accent);
            if(id==Empty)
            {
                GUI.Label(rect,"+  참가 대기 중",LobbyText(18,new Color(.50f,.57f,.67f),TextAnchor.MiddleCenter));return;
            }
            var weapon = WeaponController.ActivePlayers.FirstOrDefault(p=>p!=null && p.IsSpawned && p.OwnerClientId==id);
            string name = IsBotId(id) ? BotDisplayName(index) : weapon!=null ? weapon.GetComponent<CorePlayerState>()?.PlayerName : "연결 중…";
            if(string.IsNullOrWhiteSpace(name))name="플레이어";
            int hero = IsBotId(id) && botStates != null && index < botStates.Count
                ? Mathf.Clamp(botStates[index].character,0,3)
                : weapon!=null ? Mathf.Clamp(weapon.CharacterId,0,3) : 0;
            LobbyFill(new Rect(rect.x+10,rect.y+12,54,54),accent*.7f);
            LobbyArtwork(new Rect(rect.x+10,rect.y+12,54,54),new Rect(LobbyPortraits[hero].x+35,LobbyPortraits[hero].y,85,85));
            var nameStyle=LobbyText(18,Color.white);nameStyle.wordWrap=false;nameStyle.clipping=TextClipping.Clip;
            GUI.Label(new Rect(rect.x+76,rect.y+9,rect.width-92,27),name+(id==NetworkManager.LocalClientId?" (나)":""),nameStyle);
            GUI.Label(new Rect(rect.x+76,rect.y+39,rect.width-92,25),LobbyHeroes[hero]+"  ·  "+(readyPlayers.Contains(id)?"준비 완료":"선택 중"),LobbyText(14,new Color(.75f,.85f,.94f)));
        }
        void DrawLobbyChat(Rect rect, bool waiting)
        {
            GUI.Label(new Rect(rect.x+18,rect.y+10,rect.width-36,30),"전체 채팅",LobbyText(23,Color.white));
            LobbyFill(new Rect(rect.x+18,rect.y+48,rect.width-36,1),new Color(.4f,.5f,.6f,.5f));
            GUI.Label(new Rect(rect.x+18,rect.y+56,rect.width-36,24),"모든 참가자에게 표시됩니다.",LobbyText(16,new Color(.65f,.72f,.81f)));
            var area=new Rect(rect.x+18,rect.y+100,rect.width-36,rect.height-175);
            var style=LobbyText(16,new Color(.83f,.89f,.96f));
            float contentHeight=0;
            for(int i=0;i<lobbyChat.Count;i++)contentHeight+=style.CalcHeight(new GUIContent(lobbyChat[i].ToString()),area.width-22)+10;
            int revision=lobbyChat.Count==0?0:lobbyChat[lobbyChat.Count-1].GetHashCode();
            if(revision!=chatSeenCount){chatSeenCount=revision;chatScroll.y=Mathf.Max(0,contentHeight-area.height);}
            chatScroll=GUI.BeginScrollView(area,chatScroll,new Rect(0,0,area.width-22,Mathf.Max(area.height,contentHeight)));
            float y=0;
            if(lobbyChat.Count==0)GUI.Label(new Rect(0,0,area.width-22,60),"모든 참가자와 자유롭게 대화하세요.",style);
            for(int i=0;i<lobbyChat.Count;i++)
            {
                string line=lobbyChat[i].ToString();float h=style.CalcHeight(new GUIContent(line),area.width-22);
                GUI.Label(new Rect(0,y,area.width-22,h),line,style);y+=h+10;
            }
            GUI.EndScrollView();
            bool previous=GUI.enabled;GUI.enabled=waiting && TeamOf(NetworkManager.LocalClientId)>=0;
            GUI.SetNextControlName("LobbyChatInput");
            chatDraft=GUI.TextField(new Rect(rect.x+14,rect.yMax-52,rect.width-102,36),chatDraft,100,new GUIStyle(GUI.skin.textField){fontSize=16,padding=new RectOffset(9,9,8,4)});
            if(string.IsNullOrEmpty(chatDraft) && GUI.GetNameOfFocusedControl()!="LobbyChatInput")
                GUI.Label(new Rect(rect.x+22,rect.yMax-52,rect.width-118,36),"메시지를 입력하세요...",LobbyText(15,new Color(.55f,.64f,.75f)));
            bool send=LobbyButton(new Rect(rect.xMax-80,rect.yMax-52,66,36),"전송",new Color(.12f,.40f,.60f));
            if(Event.current.type==EventType.KeyDown && Event.current.keyCode==KeyCode.Return && GUI.GetNameOfFocusedControl()=="LobbyChatInput")
            {send=true;Event.current.Use();}
            if(send && GUI.enabled && !string.IsNullOrWhiteSpace(chatDraft)) {SendLobbyChatRpc(chatDraft);chatDraft="";GUI.FocusControl("LobbyChatInput");}
            GUI.enabled=previous;
        }
        void DrawLobby()
        {
            if(phase.Value==6 || phase.Value==1){DrawLoadingScreen();return;}
            GUI.depth=-1000;
            var oldMatrix=GUI.matrix;var oldColor=GUI.color;bool oldEnabled=GUI.enabled;
            LoadLobbyArt();
            float scale=Mathf.Min(Screen.width/1672f,Screen.height/941f);
            float offsetX=(Screen.width-1672*scale)*.5f,offsetY=(Screen.height-941*scale)*.5f;
            LobbyFill(new Rect(0,0,Screen.width,Screen.height),new Color(.035f,.055f,.09f));
            if(lobbyBackground!=null)GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),lobbyBackground,ScaleMode.ScaleAndCrop);
            GUI.matrix=Matrix4x4.TRS(new Vector3(offsetX,offsetY,0),Quaternion.identity,new Vector3(scale,scale,1));
            GUI.Label(new Rect(135,55,900,80),"게임 대기방",new GUIStyle(LobbyText(56,Color.white)){fontStyle=FontStyle.Bold});
            GUI.Label(new Rect(135,140,980,35),TeamMode?"2:2 / 화물 운송   블루 공격 · 레드 수비":"개인전 / 제한 시간 내 최다 킬",LobbyText(24,new Color(.36f,.82f,1)));
            GUI.Label(new Rect(1240,116,296,38),$"참가자 {ParticipantCount} / 4",LobbyText(25,Color.white,TextAnchor.MiddleRight));
            string roomCode=observer?.Session?.Code;
            bool hasRoomCode=!string.IsNullOrWhiteSpace(roomCode);
            GUI.Label(new Rect(1040,65,370,38),hasRoomCode?"방 코드  "+roomCode:"방 코드 확인 중…",LobbyText(23,Color.white,TextAnchor.MiddleRight));
            if(LobbyButton(new Rect(1425,65,111,38),"복사",new Color(.12f,.25f,.32f),hasRoomCode))GUIUtility.systemCopyBuffer=roomCode;
            bool waiting=phase.Value==0;
            LobbyPanel(new Rect(135,194,402,590));
            LobbyArtwork(new Rect(151,211,370,145),new Rect(152,213,368,141));
            LobbyFill(new Rect(151,356,370,50),new Color(.025f,.045f,.08f,.85f));
            GUI.Label(new Rect(163,359,346,42),"전투 아레나",LobbyText(27,Color.white));
            DrawLobbyChat(new Rect(141,422,390,352),waiting);
            LobbyPanel(new Rect(560,194,976,340));
            if(TeamMode)
            {
                for(int team=0;team<2;team++)
                {
                    float x=585+team*517;Color accent=team==0?LobbyBlue:LobbyRed;
                    LobbyArtwork(new Rect(x,219,34,38),team==0?new Rect(585,219,34,38):new Rect(1110,219,34,38));
                    GUI.Label(new Rect(x+47,215,355,42),team==0?"블루팀 / 공격":"레드팀 / 수비",LobbyText(28,accent));
                    for(int row=0;row<2;row++)DrawLobbyPlayer(new Rect(x,267+row*88,405,78),team+row*2,accent);
                    bool selected=TeamOf(NetworkManager.LocalClientId)==team;
                    bool room=Enumerable.Range(0,slots.Count).Any(i=>i%2==team && (slots[i]==Empty || BotInSlot(i)));
                    if(LobbyButton(new Rect(x,455,405,53),selected?"현재 팀":!room?"정원 마감 · 2 / 2":"이 팀으로 이동",Color.Lerp(accent,Color.black,.48f),waiting&&!selected&&room&&!sessionBusy))SelectTeamRpc(team);
                }
                GUI.Label(new Rect(990,300,112,100),"VS",LobbyText(38,Color.white,TextAnchor.MiddleCenter));
            }
            else
            {
                GUI.Label(new Rect(585,210,880,40),"참가 플레이어",LobbyText(28,Color.white));
                for(int i=0;i<4;i++)DrawLobbyPlayer(new Rect(585+(i%2)*465,267+(i/2)*112,440,90),i,LobbyBlue);
            }
            LobbyPanel(new Rect(560,546,976,226));
            GUI.Label(new Rect(584,554,880,35),"캐릭터 선택",LobbyText(25,Color.white));
            var local=WeaponController.ActivePlayers.FirstOrDefault(p=>p!=null&&p.IsSpawned&&p.IsOwner);
            for(int hero=0;hero<4;hero++)
            {
                var rect=new Rect(581+hero*237,596,221,158);
                bool selected=local!=null&&local.CharacterId==hero;
                if(LobbyButton(rect,"",new Color(.035f,.065f,.11f),waiting&&local!=null&&!sessionBusy))
                {local.SelectLobbyCharacter(hero);SetReadyRpc(false);}
                LobbyArtwork(new Rect(rect.x+2,rect.y+2,rect.width-4,86),LobbyPortraits[hero]);
                LobbyFill(new Rect(rect.x+2,rect.y+88,rect.width-4,68),selected?new Color(.03f,.20f,.32f,.95f):new Color(.025f,.045f,.08f,.95f));
                if(selected)
                {
                    LobbyBorder(new Rect(rect.x-3,rect.y-3,rect.width+6,rect.height+6),new Color(0,.6f,1,.3f),3);
                    LobbyBorder(rect,new Color(0,.78f,1),2);
                    GUI.Label(new Rect(rect.xMax-40,rect.y+5,30,30),"✓",LobbyText(28,Color.white,TextAnchor.MiddleCenter));
                }
                GUI.Label(new Rect(rect.x+16,rect.y+90,rect.width-32,33),LobbyHeroes[hero],LobbyText(26,Color.white));
                GUI.Label(new Rect(rect.x+16,rect.y+125,rect.width-25,24),LobbyHeroDetails[hero],LobbyText(16,new Color(.65f,.77f,.86f)));
            }
            string status=waiting?(TeamMode?"방장은 준비 여부와 관계없이 경기를 시작할 수 있습니다.":"2명 이상, 전원 준비 후 방장이 시작합니다."):
                phase.Value==6?$"로딩 · 경기 준비 중  {loadedPlayers.Count} / {ParticipantCount}":$"{Mathf.CeilToInt(Mathf.Max(0,(float)(deadline.Value-Now)))}초 후 경기 시작";
            GUI.Label(new Rect(560,778,976,36),string.IsNullOrEmpty(lobbyMessage)?status:lobbyMessage,LobbyText(18,new Color(.80f,.88f,.98f)));
            if(LobbyButton(new Rect(135,838,200,60),"방 나가기",new Color(.085f,.12f,.18f),!leavingRoom))LeaveLobby();
            if(LobbyButton(new Rect(355,838,300,60),fillWithBots.Value?"AI 자동 채우기: 켜짐":"AI 자동 채우기: 꺼짐",new Color(.12f,.25f,.32f),HasAuthority&&waiting&&!sessionBusy))ToggleBots();
            if(LobbyButton(new Rect(675,838,330,60),"AI 난이도: "+BotDifficultyName+" ▸",new Color(.18f,.20f,.32f),HasAuthority&&waiting&&!sessionBusy))CycleBotDifficulty();
            bool ready=readyPlayers.Contains(NetworkManager.LocalClientId);
            if(LobbyButton(new Rect(1025,838,244,60),ready?"준비 취소":"✓  준비",new Color(.075f,.37f,.68f),waiting&&TeamOf(NetworkManager.LocalClientId)>=0&&!sessionBusy))SetReadyRpc(!ready);
            if(LobbyButton(new Rect(1289,838,247,60),HasAuthority?"▶  경기 시작":"방장 시작 대기",new Color(.94f,.34f,.045f),HasAuthority&&CanStart&&!sessionBusy))StartLoading();
            GUI.matrix=oldMatrix;GUI.color=oldColor;GUI.enabled=oldEnabled;
        }
    }
}

