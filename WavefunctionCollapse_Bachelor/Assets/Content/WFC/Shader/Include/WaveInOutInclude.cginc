/*
Actual wave result
wave(node, pattern)
*/
StructuredBuffer<bool> wave_in;
#define WAVE_IN(node, pattern) wave_in[node * nb_patterns + pattern]

RWStructuredBuffer<bool> wave_out;
#define WAVE_OUT(node, pattern) wave_out[node * nb_patterns + pattern]