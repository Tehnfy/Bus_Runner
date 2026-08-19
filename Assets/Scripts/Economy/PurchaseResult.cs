/// <summary>
/// Why a purchase did or did not happen. One value per reason, so a shop row can explain itself
/// rather than just greying out — see ShopService.Explain for the wording.
///
/// In its own file, matching every other type here. Unity's script importer keys a file to the
/// first type it declares, and this enum sitting at the top of ShopService.cs was enough to leave
/// that file out of the Assembly-CSharp build entirely, with no compile error to say so.
/// </summary>
public enum PurchaseResult
{
    Purchased,

    /// <summary>Null item, or one with no usable id.</summary>
    Invalid,

    /// <summary>Switched off in the catalogue, or a level flagged as not for sale.</summary>
    Unavailable,

    /// <summary>A prerequisite is not owned yet.</summary>
    Locked,

    /// <summary>Already held, and holding a second would mean nothing.</summary>
    AlreadyOwned,

    /// <summary>A consumable already at its stock cap.</summary>
    AtCap,

    /// <summary>Not enough of the right coin.</summary>
    CannotAfford,
}
