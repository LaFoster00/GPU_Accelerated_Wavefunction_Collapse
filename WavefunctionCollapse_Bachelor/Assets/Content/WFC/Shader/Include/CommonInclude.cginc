#define FLOAT_MAX 3.402823466e+38
#define FLOAT_MIN 1.175494351e-38

uniform int nb_patterns;
uniform int width, height;
uniform bool is_periodic;
uniform int pattern_size;

/*
Actual wave result
wave(node, pattern)
*/
RWStructuredBuffer<uint> wave_data;
#define WAVE(node, pattern) wave_data[node * nb_patterns + pattern]


struct Weighting
{
    float weight;
    float log_weight;
    float distribution;
    float padding;
};
RWStructuredBuffer<Weighting> weighting;
#define WEIGHT(pattern) weighting[pattern].weight
#define LOG_WEIGHT(pattern) weighting[pattern].weight
#define DISTRIBUTION(pattern) weighting[pattern].distribution


struct Memoisation
{
    float sums_of_weights;
    float sums_of_weight_log_weights;
    float entropies;
    int num_possible_patterns;
};
RWStructuredBuffer<Memoisation> memoisation;
#define SUMS_OF_WEIGHTS(node) memoisation[node].sums_of_weights
#define SUMS_OF_WEIGHT_LOG_WEIGHTS(node) memoisation[node].sums_of_weight_log_weights
#define ENTROPIES(node) memoisation[node].entropies
#define NUM_POSSIBLE_PATTERNS(node) memoisation[node].num_possible_patterns


/*
Which patterns can be placed in which direction of the current node
propagator[uint3(pattern, otherPattern, direction)]
*/
struct Propagator
{
    uint propagatorDir[4];
};
uniform StructuredBuffer<Propagator> propagator;
#define PROPAGATOR(pattern, otherPattern, direction) propagator[pattern * nb_patterns + otherPattern].propagatorDir[direction]


struct Collapse
{
    uint is_collapsed;
    uint needs_collapse;
};

/* Neighbours of cells that changed. */
uniform StructuredBuffer<Collapse> in_collapse;
#define IN_IS_COLLAPSED(node) in_collapse[node].is_collapsed
#define IN_NEEDS_COLLAPSE(node) in_collapse[node].needs_collapse

RWStructuredBuffer<Collapse> out_collapse;
#define OUT_IS_COLLAPSED(node) out_collapse[node].is_collapsed
#define OUT_NEEDS_COLLAPSE(node) out_collapse[node].needs_collapse


struct Result
{
    uint is_possible;
    uint open_nodes;
    uint finished;
    uint padding;
};
RWStructuredBuffer<Result> result;
#define IS_POSSIBLE result[0].is_possible
#define OPEN_NODES result[0].open_nodes
#define FINISHED result[0].finished

#define TRUE 1u
#define FALSE 0u
