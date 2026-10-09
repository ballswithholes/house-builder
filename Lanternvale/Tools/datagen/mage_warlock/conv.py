CONVENTIONS = [
    "Magnitudes are WoW Classic 1.12 rank-1 numbers; perLevel lands the last rankLevels entry on the real max rank (avg of min/max).",
    "Channels (channeled: true): the ability's effects list is the per-tick payload, applied channelTicks times over castTime.",
    "Rank-scaled buffs/debuffs scale through their aura mods: StatModDef.values (exact WoW value per rank of the applying ability) or StatModDef.perLevel (demon auras, level-scaled). Aura tick/proc effects and absorbs use perLevel against the applying ability's effective level.",
    "Rank-dependent cast times and durations use the max-rank value (one castTime / duration per ability).",
    "Talent Proc passives whose per-rank 'values' are one-hot (e.g. [0,100,0]) select a different aura per rank.",
    "AbilityMod 'Damage' = direct Damage effects; 'Effect' = DoT/HoT ticks, absorbs and aura stat amounts (per Enums.cs).",
    "Talents that modify demon abilities (AbilityMod on warlock_imp_* etc.) are owner talents that must apply to the pet's abilities.",
]
