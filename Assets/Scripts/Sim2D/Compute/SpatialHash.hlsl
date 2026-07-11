static const int2 offsets2D[9] =
{
	int2(-1, 1),
	int2(0, 1),
	int2(1, 1),
	int2(-1, 0),
	int2(0, 0),
	int2(1, 0),
	int2(-1, -1),
	int2(0, -1),
	int2(1, -1),
};

// Constants used for hashing
static const uint hashK1 = 15823;
static const uint hashK2 = 9737333;

// Convert floating point position into an integer cell coordinate
int2 GetCell2D(float2 position, float radius)
{
	return (int2)floor(position / radius);
}

// Hash cell coordinate to a single unsigned integer
uint HashCell2D(int2 cell)
{
	cell = (uint2)cell;
	uint a = cell.x * hashK1;
	uint b = cell.y * hashK2;
	return (a + b);
}

uint KeyFromHash(uint hash, uint tableSize)
{
	return hash % tableSize;
}

// HLSL has no lambda/delegate equivalent for reusable loop bodies, so this macro
// keeps neighbour traversal inline while preserving caller-side continue/break use.
#define FOR_EACH_HASH_NEIGHBOR(ORIGIN_CELL, TABLE_SIZE, OFFSETS_BUFFER, KEYS_BUFFER, NEIGHBOR_INDEX) \
	for (int _neighborCellIndex = 0; _neighborCellIndex < 9; _neighborCellIndex++) \
	{ \
		uint _neighborHash = HashCell2D((ORIGIN_CELL) + offsets2D[_neighborCellIndex]); \
		uint _neighborKey = KeyFromHash(_neighborHash, (TABLE_SIZE)); \
		uint _neighborCursor = (OFFSETS_BUFFER)[_neighborKey]; \
		while (_neighborCursor < (TABLE_SIZE)) \
		{ \
			uint NEIGHBOR_INDEX = _neighborCursor++; \
			if ((KEYS_BUFFER)[NEIGHBOR_INDEX] != _neighborKey) break;

#define END_FOR_EACH_HASH_NEIGHBOR \
		} \
	}
