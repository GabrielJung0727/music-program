namespace Auralis.Protocol;

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

    // 카탈로그 · 라이브러리
    public const string Catalog = "catalog";
    public const string Search = "search";
    public const string Graph = "graph";
    public const string ScanLibrary = "scan_library";
    public const string LinkStreaming = "link_streaming";

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

    // AATP · 엔드포인트
    public const string ClockPing = "clock_ping";
    public const string ClockPong = "clock_pong";
    public const string ClockReport = "clock_report";
    public const string OutputHello = "output_hello";
    public const string Caps = "caps";
    public const string Volume = "volume";
    public const string SetVolume = "set_volume";
    public const string Endpoints = "endpoints";
    public const string AatpAudio = "aatp_audio";
    public const string Snapshot = "snapshot";
}
