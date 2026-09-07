#ifndef DRAGONLOOT_COIN_COMMON_INCLUDED
#define DRAGONLOOT_COIN_COMMON_INCLUDED

#include "CoinInput.hlsl"

half CoinSchlickFresnel(half ndotv, half power)
{
    half inv = 1.0h - saturate(ndotv);
    return pow(inv, max(power, 0.01h));
}

#endif
