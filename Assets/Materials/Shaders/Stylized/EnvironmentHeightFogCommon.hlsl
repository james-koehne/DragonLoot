#ifndef DRAGONLOOT_ENVIRONMENT_HEIGHT_FOG_COMMON_INCLUDED
#define DRAGONLOOT_ENVIRONMENT_HEIGHT_FOG_COMMON_INCLUDED

// Expects UnityPerMaterial to provide:
//   _HeightFogColor, _HeightFogDeepColor, _HeightFogDensity, _HeightFogFalloff,
//   _FogHeight, _HeightFogSoftness, _HeightFogStrength

// Optical depth only along the ray segment that sits at/below FogHeight.
half DragonLootHeightFogOpticalDepth(float3 rayOriginWS, float3 rayDirWS, float tNear, float tFar)
{
	half y0 = rayOriginWS.y;
	half dy = rayDirWS.y;
	half fogH = _FogHeight;

	// Clip [tNear, tFar] to where y(t) <= fogH.
	float tLo = tNear;
	float tHi = tFar;
	if (abs(dy) < 1e-5h)
	{
		if (y0 > fogH)
			return 0.0h;
	}
	else
	{
		float tPlane = (fogH - y0) / dy;
		if (dy > 0.0h)
		{
			// Moving up: fog only before crossing the plane.
			tHi = min(tHi, tPlane);
		}
		else
		{
			// Moving down: fog only after crossing the plane.
			tLo = max(tLo, tPlane);
		}
	}

	if (tHi <= tLo + 1e-5)
		return 0.0h;

	half falloff = max(_HeightFogFalloff, 0.0h);
	half dens = max(_HeightFogDensity, 0.0h);
	half k = falloff * dy;
	half segment = max(tHi - tLo, 0.0h);

	half optical;
	if (abs(k) < 1e-4h)
		optical = dens * exp(-falloff * (y0 - fogH)) * segment;
	else
	{
		half base = dens * exp(-falloff * (y0 - fogH));
		optical = base * (exp(-k * tLo) - exp(-k * tHi)) / k;
	}

	return max(optical, 0.0h);
}

half3 DragonLootApplyHeightFog(half3 color, float3 positionWS)
{
#if !defined(_HEIGHTFOG)
	return color;
#else
	half strength = saturate(_HeightFogStrength);
	if (strength <= 1e-4h)
		return color;

	// Surfaces above the fog plane keep their lit colour (soft blend across Height Softness).
	half soft = max(_HeightFogSoftness, 1e-4h);
	half belowMask = 1.0h - smoothstep(_FogHeight - soft, _FogHeight, positionWS.y);
	if (belowMask <= 1e-4h)
		return color;

	float3 cameraWS = GetCurrentViewPosition();
	float3 ray = positionWS - cameraWS;
	float tFar = length(ray);
	if (tFar <= 1e-4)
		return color;

	float3 rayDirWS = ray / tFar;
	half optical = DragonLootHeightFogOpticalDepth(cameraWS, rayDirWS, 0.0, tFar);
	half fogAmount = saturate((1.0h - exp(-optical)) * strength) * belowMask;
	if (fogAmount <= 1e-4h)
		return color;

	half yLow = min(cameraWS.y, positionWS.y);
	half softBand = max(soft * 8.0h, 1.0h);
	half depthT = saturate(1.0h - exp(-optical * 0.65h));
	half heightT = 1.0h - saturate((yLow - (_FogHeight - softBand * 0.5h)) / softBand);
	half abyss = saturate(max(depthT, heightT * depthT));

	half3 fogColor = lerp(_HeightFogColor.rgb, _HeightFogDeepColor.rgb, abyss);
	return lerp(color, fogColor, fogAmount);
#endif
}

#endif
