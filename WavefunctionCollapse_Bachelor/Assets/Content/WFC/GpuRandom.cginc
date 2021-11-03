// Based on Unity.Mathematics.Random

static uint state = 0;

static uint random_next_state()
{
    const uint t = state;
    state ^= state << 13;
    state ^= state >> 17;
    state ^= state << 5;
    return t;
}


/// <summary>Returns the bit pattern of a uint as a float.</summary>
static float random_as_float(uint x) { return asfloat((int)x); }

/// <summary>Returns the bit pattern of a uint2 as a float2.</summary>
static float2 random_as_float(uint2 x) { return float2(random_as_float(x.x), random_as_float(x.y)); }

/// <summary>Returns the bit pattern of a uint3 as a float3.</summary>
static float3 random_as_float(uint3 x) { return float3(random_as_float(x.x), random_as_float(x.y), random_as_float(x.z)); }

/// <summary>Returns the bit pattern of a uint4 as a float4.</summary>
static float4 random_as_float(uint4 x) { return float4(random_as_float(x.x), random_as_float(x.y), random_as_float(x.z), random_as_float(x.w)); }


/// <summary>
/// Initialized the state of the Random instance with a given seed value. The seed must be non-zero.
/// </summary>
static void random_init_state(uint seed = 0x6E624EB7u)
{
    state = seed;
    random_next_state();
}

/// <summary>Returns a uniformly random bool value.</summary>
static bool random_next_bool()
{
    return (random_next_state() & 1) == 1;
}

/// <summary>Returns a uniformly random bool2 value.</summary>
static bool2 random_next_bool2()
{
    uint v = random_next_state();
    return (uint2(v.xx) & uint2(1, 2)) == 0;
}

/// <summary>Returns a uniformly random bool3 value.</summary>
static bool3 random_next_bool3()
{
    uint v = random_next_state();
    return (uint3(v.xxx) & uint3(1, 2, 4)) == 0;
}

/// <summary>Returns a uniformly random bool4 value.</summary>
static bool4 random_next_bool4()
{
    uint v = random_next_state();
    return (uint4(v.xxxx) & uint4(1, 2, 4, 8)) == 0;
}


/// <summary>Returns a uniformly random int value in the interval [-2147483647, 2147483647].</summary>
static int random_next_int()
{
    return (int)random_next_state() ^ -2147483648;
}

/// <summary>Returns a uniformly random int2 value with all components in the interval [-2147483647, 2147483647].</summary>
static int2 random_next_int2()
{
    return int2((int)random_next_state(), (int)random_next_state()) ^ -2147483648;
}

/// <summary>Returns a uniformly random int3 value with all components in the interval [-2147483647, 2147483647].</summary>
static int3 random_next_int3()
{
    return int3((int)random_next_state(), (int)random_next_state(), (int)random_next_state()) ^ -2147483648;
}

/// <summary>Returns a uniformly random int4 value with all components in the interval [-2147483647, 2147483647].</summary>
static int4 random_next_int4()
{
    return int4((int)random_next_state(), (int)random_next_state(), (int)random_next_state(), (int)random_next_state()) ^ -2147483648;
}

/// <summary>Returns a uniformly random uint value in the interval [0, 4294967294].</summary>
static uint random_next_uint()
{
    return random_next_state() - 1u;
}

/// <summary>Returns a uniformly random uint2 value with all components in the interval [0, 4294967294].</summary>
static uint2 random_next_uint2()
{
    return uint2(random_next_state(), random_next_state()) - 1u;
}

/// <summary>Returns a uniformly random uint3 value with all components in the interval [0, 4294967294].</summary>
static uint3 random_next_uint3()
{
    return uint3(random_next_state(), random_next_state(), random_next_state()) - 1u;
}

/// <summary>Returns a uniformly random uint4 value with all components in the interval [0, 4294967294].</summary>
static uint4 random_next_uint4()
{
    return uint4(random_next_state(), random_next_state(), random_next_state(), random_next_state()) - 1u;
}

/// <summary>Returns a uniformly random float value in the interval [0, 1).</summary>
static float random_next_float()
{
    return random_as_float(0x3f800000 | (random_next_state() >> 9)) - 1.0f;
}

/// <summary>Returns a uniformly random float2 value with all components in the interval [0, 1).</summary>
static float2 random_next_float2()
{
    return random_as_float(0x3f800000 | (uint2(random_next_state(), random_next_state()) >> 9)) - 1.0f;
}

/// <summary>Returns a uniformly random float3 value with all components in the interval [0, 1).</summary>
static float3 random_next_float3()
{
    return random_as_float(0x3f800000 | (uint3(random_next_state(), random_next_state(), random_next_state()) >> 9)) - 1.0f;
}

/// <summary>Returns a uniformly random float4 value with all components in the interval [0, 1).</summary>
static float4 random_next_float4()
{
    return random_as_float(0x3f800000 | (uint4(random_next_state(), random_next_state(), random_next_state(), random_next_state()) >> 9)) - 1.0f;
}


/// <summary>Returns a uniformly random float value in the interval [0, max).</summary>
static float random_next_float(const float max) { return random_next_float() * max; }

/// <summary>Returns a uniformly random float2 value with all components in the interval [0, max).</summary>
static float2 random_next_float2(const float2 max) { return random_next_float2() * max; }

/// <summary>Returns a uniformly random float3 value with all components in the interval [0, max).</summary>
static float3 random_next_float3(const float3 max) { return random_next_float3() * max; }

/// <summary>Returns a uniformly random float4 value with all components in the interval [0, max).</summary>
static float4 random_next_float4(const float4 max) { return random_next_float4() * max; }


/// <summary>Returns a uniformly random float value in the interval [min, max).</summary>
static float random_next_float(const float min, const float max) { return random_next_float() * (max - min) + min; }

/// <summary>Returns a uniformly random float2 value with all components in the interval [min, max).</summary>
static float2 random_next_float2(const float2 min, const float2 max) { return random_next_float2() * (max - min) + min; }

/// <summary>Returns a uniformly random float3 value with all components in the interval [min, max).</summary>
static float3 random_next_float3(const float3 min, const float3 max) { return random_next_float3() * (max - min) + min; }

/// <summary>Returns a uniformly random float4 value with all components in the interval [min, max).</summary>
static float4 random_next_float4(const float4 min, const float4 max) { return random_next_float4() * (max - min) + min; }