// namespace core_engine;
//
// /// <summary>
// /// The token indicates the current position of the process instance's execution.
// /// </summary>
// public class Token
// {
//     /// <summary>
//     /// The unique ID of the token.
//     /// </summary>
//     public required Guid Id { get; set; } = Guid.NewGuid();
//     
//     /// <summary>
//     /// The ID of the node where the token is currently located.
//     /// </summary>
//     public required ProcessFlowNode ActualFlowNode { get; set; }
//     
//     public int RemainingRetries { get; set; } = 3;
//     
//     public DateTime? LockUntil { get; set; } // Wenn gesetzt, ist die Aktivity gesperrt, da sie bearbeitet wird
//
// }