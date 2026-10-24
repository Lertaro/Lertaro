namespace Lertaro.Core.Indexer.Usn;

// What a batch of USN records may spend, decided by where it came from.
//
// A live batch is a handful of records for changes nothing has recorded yet, so it can afford a
// directory fan-out per batch and a file handle query per touched object. A replay is the catch-up of
// everything that happened while no monitor was reading -- routinely tens of thousands of records, most
// of them re-describing what the scan behind it already captured -- and paying the live path's costs
// there competes with the very drain those records are part of.
public enum UsnApplyOrigin
{
    Live,
    Replay,
}
