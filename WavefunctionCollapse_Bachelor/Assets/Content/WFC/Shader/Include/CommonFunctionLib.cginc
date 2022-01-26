#include "Include/CommonInclude.cginc"
#include "Include/Utils.cginc"

RWStructuredBuffer<uint> wave_out;
#define WAVE_OUT(node, pattern) wave_out[node * nb_patterns + pattern]

/* Removes pattern from cell and marks the surrounding cells for update. */
void Ban(int node, uint2 nodeCoord, int pattern)
{
    /*
     *Check if node has already been removed this iteration since pattern can be checked multiple times in one
     *iteration. Removing the node twice will lead to wrong entropy data and a lot of rejections.
     */
    if (WAVE_OUT(node, pattern) == FALSE)
    {
        return;
    }
    WAVE_OUT(node, pattern) = FALSE;
    
    OPEN_NODES = TRUE;

    NUM_POSSIBLE_PATTERNS(node) -= 1;
    SUMS_OF_WEIGHTS(node) -= WEIGHT(pattern);
    SUMS_OF_WEIGHT_LOG_WEIGHTS(node) -= LOG_WEIGHT(pattern);

    float sum = SUMS_OF_WEIGHTS(node);
    ENTROPIES(node) = log(sum) - SUMS_OF_WEIGHT_LOG_WEIGHTS(node) / sum;
    if (NUM_POSSIBLE_PATTERNS(node) <= 0)
    {
        IS_POSSIBLE = FALSE;
    }

    OUT_IS_COLLAPSED(node) = TRUE;
    /* Mark the neighbouring nodes for collapse and update info */
    for (int direction = 0; direction < 4; direction++)
    {
        /* Generate neighbour coordinate */
        int x2 = (int)nodeCoord.x + directions_x[direction];
        int y2 = (int)nodeCoord.y + directions_y[direction];

        if (is_periodic)
        {
            x2 = (x2 + width) % width;
            y2 = (y2 + height) % height;
        }
        else if (!is_periodic && (x2 < 0
                                       || y2 < 0
                                       || x2 + pattern_size >= width
                                       || y2 + pattern_size >= height))
        {
            continue;
        }

        /* Add neighbour to hash set of pen neighbours. */
        const int node2 = y2 * width + x2;
        OUT_NEEDS_COLLAPSE(node2) = TRUE;
    }
}