#include "Include/CommonInclude.cginc"
#include "Include/Utils.cginc"

/* Removes pattern from cell and marks the surrounding cells for update. */
void Ban(uint node, uint2 nodeCoord, int pattern)
{
    OPEN_NODES = TRUE;
    WAVE(node, pattern) = FALSE;
    
    NUM_POSSIBLE_PATTERNS(node) -= 1;
    SUMS_OF_WEIGHTS(node) -= WEIGHT(pattern);
    SUMS_OF_WEIGHT_LOG_WEIGHTS(node) -= LOG_WEIGHT(pattern);

    const float sum = SUMS_OF_WEIGHTS(node);
    const float sumLog = log(sum);
    const float sumsLogDivideSum = SUMS_OF_WEIGHT_LOG_WEIGHTS(node) / sum;
    //ENTROPIES(node) = sumLog - sumsLogDivideSum;
    if (NUM_POSSIBLE_PATTERNS(node) <= 0)
    {
        IS_POSSIBLE = FALSE;
    }
    
    /* Mark the neighbouring nodes for collapse and update info */
    OUT_IS_COLLAPSED(node) = TRUE;
    for (int direction = 0; direction < 4; direction++)
    {
        /* Generate neighbour coordinate */
        int x2 = nodeCoord.x + directions_x[direction];
        int y2 = nodeCoord.y + directions_y[direction];

        if (is_periodic)
        {
            x2 = ((uint)(x2 + width)) % width;
            y2 = ((uint)(y2 + height)) % height;
        }
        else if (!is_periodic && (x2 < 0
                               || y2 < 0
                               || x2 + pattern_size >= width 
                               || y2 + pattern_size >= height))
        {
            continue;
        }

        const uint node2 = y2 * width + x2;
        OUT_NEEDS_COLLAPSE(node2) = TRUE;
    }
}