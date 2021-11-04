/*
* A direction is represented by an unsigned integer in the range [0; 3].
* The x and y values of the direction can be retrieved in these tables.
*/
static int directions_x[4] =  {0, -1, 1, 0};
static int directions_y[4] =  {-1, 0, 0, 1};

static int opposite_direction[4] = { 3, 2, 1, 0 };

static int all_dir_int_null[4] = { 0, 0, 0, 0 };