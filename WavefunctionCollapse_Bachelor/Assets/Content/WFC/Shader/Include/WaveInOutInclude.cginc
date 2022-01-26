/*
Actual wave result
wave(node, pattern)
*/
StructuredBuffer<uint> wave_in;
#define WAVE_IN(node, pattern) wave_in[node * nb_patterns + pattern]

RWStructuredBuffer<uint> wave_out;
#define WAVE_OUT(node, pattern) wave_out[node * nb_patterns + pattern]