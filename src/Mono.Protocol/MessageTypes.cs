namespace Mono.Protocol;

public static class MessageTypes
{
    // 세션
    public const string Hello = "hello";
    public const string Welcome = "welcome";
    public const string Error = "error";
    public const string Pair = "pair";
    public const string PairingIssued = "pairing_issued";
    public const string Redeem = "redeem";

    // 룸
    public const string CreateRoom = "create_room";
    public const string JoinRoom = "join_room";
    public const string LeaveRoom = "leave_room";
    public const string ListRooms = "list_rooms";
    public const string RoomState = "room_state";
    public const string Invite = "invite";
    public const string Kick = "kick";
    public const string TransferHost = "transfer_host";
    public const string SetRole = "set_role";
    public const string Spectate = "spectate";
    public const string SetPolicy = "set_policy";
    public const string SetDsp = "set_dsp";
    public const string SetConvolutionIr = "set_convolution_ir";
    public const string SetEasyEq = "set_easy_eq";
    public const string SetSpeakerSetup = "set_speaker_setup";
    public const string SetHeadroom = "set_headroom";
    public const string SetDeviceEq = "set_device_eq";
    public const string SyncProbe = "sync_probe";
    public const string SetSourceMode = "set_source_mode";
    public const string SetRoomFlags = "set_room_flags";

    // 큐 · 트랜스포트
    public const string Enqueue = "enqueue";
    public const string RequestTrack = "request_track";
    public const string ApproveRequest = "approve_request";
    public const string RejectRequest = "reject_request";
    public const string RemoveQueue = "remove_queue";
    public const string MoveQueue = "move_queue";
    public const string ClearQueue = "clear_queue";
    public const string Play = "play";
    public const string Pause = "pause";
    public const string Seek = "seek";
    public const string SeekPin = "seek_pin";
    public const string Skip = "skip";
    public const string JumpTo = "jump_to";
    public const string Resync = "resync";
    public const string Timeline = "timeline";

    // 소셜
    public const string Pin = "pin";
    public const string RemovePin = "remove_pin";
    public const string Chat = "chat";
    public const string React = "react";
    public const string FollowArtist = "follow_artist";
    public const string FollowHost = "follow_host";
    public const string LinerPage = "liner_page";
    public const string LinerScroll = "liner_scroll";

    // 스마트 오토플레이
    public const string AutoplayCandidates = "autoplay_candidates";
    public const string ChooseAutoplay = "choose_autoplay";

    // 카탈로그 · 라이브러리
    public const string Catalog = "catalog";
    public const string Search = "search";
    public const string Graph = "graph";
    public const string ScanLibrary = "scan_library";
    public const string Folders = "folders";
    public const string LinkStreaming = "link_streaming";
    public const string ReactionHeatmap = "reaction_heatmap";
    public const string WikiBio = "wiki_bio";

    // 멀티 디바이스 존 (싱글 플레이)
    public const string CreateZone = "create_zone";
    public const string RenameZone = "rename_zone";
    public const string SetZoneMode = "set_zone_mode";
    public const string ZoneAddMember = "zone_add_member";
    public const string ZoneRemoveMember = "zone_remove_member";
    public const string DeleteZone = "delete_zone";
    public const string ListZones = "list_zones";

    // 아카이브 · 개인 라이브러리
    public const string EndSession = "end_session";
    public const string Archive = "archive";
    public const string Archives = "archives";
    public const string History = "history";
    public const string Playlists = "playlists";
    public const string CreatePlaylist = "create_playlist";
    public const string LoadPlaylist = "load_playlist";
    public const string ExportM3u = "export_m3u";
    public const string ShareSession = "share_session";

    // MATP · 엔드포인트
    public const string ClockPing = "clock_ping";
    public const string ClockPong = "clock_pong";
    public const string ClockReport = "clock_report";
    public const string OutputHello = "output_hello";
    public const string Caps = "caps";
    public const string Volume = "volume";
    public const string SetVolume = "set_volume";
    public const string Endpoints = "endpoints";
    public const string MatpAudio = "matp_audio";
    public const string Snapshot = "snapshot";
}
