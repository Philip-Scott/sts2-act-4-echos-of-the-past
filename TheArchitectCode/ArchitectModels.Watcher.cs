using MegaCrit.Sts2.Core.Models;
using TheArchitect.TheArchitectCode.Enchantments;
using TheArchitect.TheArchitectCode.Powers;
using TheArchitect.TheArchitectCode.Relics;

namespace TheArchitect.TheArchitectCode;

internal static partial class ArchitectModels
{
    internal static readonly ModelId WrathEnchantmentId = new("ENCHANTMENT", "THEARCHITECT-WRATH");
    internal static readonly ModelId CalmEnchantmentId = new("ENCHANTMENT", "THEARCHITECT-CALM");
    internal static readonly ModelId WrathStanceId = new("POWER", "THEARCHITECT-WRATH_STANCE_POWER");
    internal static readonly ModelId CalmStanceId = new("POWER", "THEARCHITECT-CALM_STANCE_POWER");
    internal static readonly ModelId VioletLotusId = new("RELIC", "THEARCHITECT-VIOLET_LOTUS");

    internal static Wrath WrathEnchantment => ModelDb.GetById<Wrath>(WrathEnchantmentId);
    internal static Calm CalmEnchantment => ModelDb.GetById<Calm>(CalmEnchantmentId);
    internal static WrathStancePower WrathStance => ModelDb.GetById<WrathStancePower>(WrathStanceId);
    internal static CalmStancePower CalmStance => ModelDb.GetById<CalmStancePower>(CalmStanceId);
    internal static VioletLotus VioletLotus => ModelDb.GetById<VioletLotus>(VioletLotusId);
}
