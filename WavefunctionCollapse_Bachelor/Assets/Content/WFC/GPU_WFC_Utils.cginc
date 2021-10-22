struct FloatArray1D
{
    int width;
    int count;
    Texture2D<float> data;

    float get(int index)
    {
        return data[int2(index % width, index / width)];
    }
};

struct FloatArray2D
{
    int width;
    int countX;
    int countY;
    Texture2D<float> data;

    float get(int x, int y)
    {
        return data[int2(x, y)];
    }
};

struct FloatArray3D
{
    int width;
    int countX;
    int countY;
    int countZ;
    Texture3D<float> data;

    float get(int x, int y, int z)
    {
        return data[int3(x, y, z)];
    }
};

struct DoubleArray1D
{
    int width;
    int count;
    Texture2D<double> data;

    double get(int index)
    {
        const int2 coord = int2(index % width, index / width);
        return data[coord];
    }
};

struct DoubleArray2D
{
    int width;
    int countX;
    int countY;
    Texture2D<double> data;

    double get(int x, int y)
    {
        return data[int2(x, y)];
    }
};

struct DoubleArray3D
{
    int width;
    int countX;
    int countY;
    int countZ;
    Texture3D<double> data;

    double get(int x, int y, int z)
    {
        return data[int3(x, y, z)];
    }
};

struct IntArray1D
{
    int width;
    int count;
    Texture2D<int> data;

    int get(int index)
    {
        const int2 coord = int2(index % width, index / width);
        return data[coord];
    }
};

struct IntArray2D
{
    int width;
    int countX;
    int countY;
    Texture2D<int> data;

    int get(int x, int y)
    {
        return data[int2(x, y)];
    }
};

struct IntArray3D
{
    int width;
    int countX;
    int countY;
    int countZ;
    Texture3D<int> data;

    int get(int x, int y, int z)
    {
        return data[int3(x, y, z)];
    }
};

struct BoolArray1D
{
    int width;
    int count;
    Texture2D<bool> data;

    bool get(int index)
    {
        const int2 coord = int2(index % width, index / width);
        return data[coord];
    }
};

struct BoolArray2D
{
    int width;
    int countX;
    int countY;
    Texture2D<bool> data;

    bool get(int x, int y)
    {
        return data[int2(x, y)];
    }
};

struct BoolArray3D
{
    int width;
    int countX;
    int countY;
    int countZ;
    Texture3D<bool> data;

    bool get(int3 coord)
    {
        return data[coord];
    }
};