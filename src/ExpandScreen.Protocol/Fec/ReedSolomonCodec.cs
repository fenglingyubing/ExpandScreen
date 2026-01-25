using System.Diagnostics;

namespace ExpandScreen.Protocol.Fec
{
    /// <summary>
    /// Reed-Solomon 纠删码（GF(256)），用于 FEC（BOTH-302）。
    /// 支持 dataShards + parityShards 的系统码：前 dataShards 为原始数据分片，后 parityShards 为校验分片。
    /// </summary>
    public sealed class ReedSolomonCodec
    {
        private readonly int _dataShards;
        private readonly int _parityShards;
        private readonly int _totalShards;
        private readonly byte[][] _matrix; // totalShards x dataShards (systematic)

        public int DataShards => _dataShards;
        public int ParityShards => _parityShards;
        public int TotalShards => _totalShards;

        public ReedSolomonCodec(int dataShards, int parityShards)
        {
            if (dataShards <= 0) throw new ArgumentOutOfRangeException(nameof(dataShards));
            if (parityShards <= 0) throw new ArgumentOutOfRangeException(nameof(parityShards));

            _dataShards = dataShards;
            _parityShards = parityShards;
            _totalShards = dataShards + parityShards;

            _matrix = BuildSystematicMatrix(_totalShards, _dataShards);
        }

        /// <summary>
        /// 用 data 分片生成 parity 分片（shards 长度必须为 TotalShards；前 dataShards 已填充）。
        /// </summary>
        public void EncodeParity(byte[][] shards, int shardLength)
        {
            ValidateShards(shards, shardLength);

            // parity = matrixRow * data
            for (int p = 0; p < _parityShards; p++)
            {
                var output = shards[_dataShards + p];
                Array.Clear(output, 0, shardLength);

                var row = _matrix[_dataShards + p];
                for (int d = 0; d < _dataShards; d++)
                {
                    Galois.AddMul(output, shards[d], row[d], shardLength);
                }
            }
        }

        /// <summary>
        /// 恢复缺失分片并补齐 parity（最多可恢复 parityShards 个缺失分片）。
        /// shardPresent 表示 shards[i] 是否有效。
        /// </summary>
        public void DecodeMissing(byte[][] shards, bool[] shardPresent, int shardLength)
        {
            ValidateShards(shards, shardLength);
            if (shardPresent.Length != _totalShards)
            {
                throw new ArgumentException("shardPresent length mismatch", nameof(shardPresent));
            }

            int presentCount = 0;
            for (int i = 0; i < _totalShards; i++)
            {
                if (shardPresent[i]) presentCount++;
            }

            if (presentCount < _dataShards)
            {
                throw new InvalidOperationException($"Not enough shards to reconstruct: present={presentCount}, required={_dataShards}");
            }

            bool anyDataMissing = false;
            for (int i = 0; i < _dataShards; i++)
            {
                if (!shardPresent[i])
                {
                    anyDataMissing = true;
                    break;
                }
            }

            if (anyDataMissing)
            {
                int[] validIndices = new int[_dataShards];
                int vi = 0;
                for (int i = 0; i < _totalShards && vi < _dataShards; i++)
                {
                    if (shardPresent[i])
                    {
                        validIndices[vi++] = i;
                    }
                }

                var subMatrix = new byte[_dataShards][];
                for (int r = 0; r < _dataShards; r++)
                {
                    subMatrix[r] = (byte[])_matrix[validIndices[r]].Clone();
                }

                var dataDecodeMatrix = Matrix.Invert(subMatrix);

                var tempOutputs = new byte[_dataShards][];
                for (int i = 0; i < _dataShards; i++)
                {
                    tempOutputs[i] = new byte[shardLength];
                    for (int r = 0; r < _dataShards; r++)
                    {
                        byte coef = dataDecodeMatrix[i][r];
                        if (coef == 0) continue;
                        Galois.AddMul(tempOutputs[i], shards[validIndices[r]], coef, shardLength);
                    }
                }

                for (int i = 0; i < _dataShards; i++)
                {
                    if (!shardPresent[i])
                    {
                        Buffer.BlockCopy(tempOutputs[i], 0, shards[i], 0, shardLength);
                        shardPresent[i] = true;
                    }
                }
            }

            // 补齐 parity（无论 parity 是否缺失，重新生成更简单）
            for (int i = 0; i < _parityShards; i++)
            {
                shardPresent[_dataShards + i] = true;
            }
            EncodeParity(shards, shardLength);
        }

        private void ValidateShards(byte[][] shards, int shardLength)
        {
            if (shards.Length != _totalShards)
            {
                throw new ArgumentException($"shards length mismatch: expected {_totalShards}, got {shards.Length}", nameof(shards));
            }

            if (shardLength <= 0) throw new ArgumentOutOfRangeException(nameof(shardLength));
            for (int i = 0; i < shards.Length; i++)
            {
                if (shards[i] == null) throw new ArgumentNullException(nameof(shards), $"shards[{i}] is null");
                if (shards[i].Length < shardLength)
                {
                    throw new ArgumentException($"shards[{i}] too small: {shards[i].Length} < {shardLength}", nameof(shards));
                }
            }
        }

        private static byte[][] BuildSystematicMatrix(int totalShards, int dataShards)
        {
            // 1) Vandermonde matrix: totalShards x dataShards
            var vandermonde = new byte[totalShards][];
            for (int r = 0; r < totalShards; r++)
            {
                vandermonde[r] = new byte[dataShards];
                byte x = (byte)r;
                byte value = 1;
                for (int c = 0; c < dataShards; c++)
                {
                    vandermonde[r][c] = value;
                    value = Galois.Mul(value, x);
                }
            }

            // 2) 取前 dataShards 行，求逆，使其变为单位阵（systematic）
            var top = new byte[dataShards][];
            for (int i = 0; i < dataShards; i++)
            {
                top[i] = (byte[])vandermonde[i].Clone();
            }

            var topInverse = Matrix.Invert(top);

            // 3) systematic = vandermonde * topInverse
            var result = new byte[totalShards][];
            for (int r = 0; r < totalShards; r++)
            {
                result[r] = new byte[dataShards];
                for (int c = 0; c < dataShards; c++)
                {
                    byte acc = 0;
                    for (int k = 0; k < dataShards; k++)
                    {
                        acc ^= Galois.Mul(vandermonde[r][k], topInverse[k][c]);
                    }
                    result[r][c] = acc;
                }
            }

            // sanity: 前 dataShards 行应是单位阵
            for (int r = 0; r < dataShards; r++)
            {
                for (int c = 0; c < dataShards; c++)
                {
                    byte expected = (byte)(r == c ? 1 : 0);
                    Debug.Assert(result[r][c] == expected);
                }
            }

            return result;
        }

        private static class Galois
        {
            private const int FieldSize = 256;
            private const int PrimitivePolynomial = 0x11D;
            private static readonly byte[] Exp = new byte[FieldSize * 2];
            private static readonly byte[] Log = new byte[FieldSize];

            static Galois()
            {
                int x = 1;
                for (int i = 0; i < FieldSize - 1; i++)
                {
                    Exp[i] = (byte)x;
                    Log[x] = (byte)i;
                    x <<= 1;
                    if (x >= FieldSize)
                    {
                        x ^= PrimitivePolynomial;
                    }
                }

                for (int i = FieldSize - 1; i < Exp.Length; i++)
                {
                    Exp[i] = Exp[i - (FieldSize - 1)];
                }

                Log[0] = 0;
            }

            public static byte Mul(byte a, byte b)
            {
                if (a == 0 || b == 0) return 0;
                int log = Log[a] + Log[b];
                return Exp[log];
            }

            public static byte Div(byte a, byte b)
            {
                if (a == 0) return 0;
                if (b == 0) throw new DivideByZeroException();
                int log = Log[a] - Log[b];
                if (log < 0) log += 255;
                return Exp[log];
            }

            public static byte Inv(byte a)
            {
                if (a == 0) throw new DivideByZeroException();
                return Exp[255 - Log[a]];
            }

            public static void AddMul(byte[] destination, byte[] source, byte coefficient, int length)
            {
                if (coefficient == 0) return;
                if (coefficient == 1)
                {
                    for (int i = 0; i < length; i++)
                    {
                        destination[i] ^= source[i];
                    }
                    return;
                }

                for (int i = 0; i < length; i++)
                {
                    destination[i] ^= Mul(source[i], coefficient);
                }
            }
        }

        private static class Matrix
        {
            public static byte[][] Invert(byte[][] matrix)
            {
                int n = matrix.Length;
                if (n == 0) throw new ArgumentException("empty matrix", nameof(matrix));
                for (int i = 0; i < n; i++)
                {
                    if (matrix[i].Length != n)
                    {
                        throw new ArgumentException("matrix must be square", nameof(matrix));
                    }
                }

                // Augment with identity matrix
                var work = new byte[n][];
                for (int r = 0; r < n; r++)
                {
                    work[r] = new byte[n * 2];
                    Buffer.BlockCopy(matrix[r], 0, work[r], 0, n);
                    work[r][n + r] = 1;
                }

                // Gauss-Jordan elimination in GF(256)
                for (int col = 0; col < n; col++)
                {
                    int pivot = col;
                    for (; pivot < n; pivot++)
                    {
                        if (work[pivot][col] != 0) break;
                    }

                    if (pivot == n)
                    {
                        throw new InvalidOperationException("matrix is singular");
                    }

                    if (pivot != col)
                    {
                        (work[pivot], work[col]) = (work[col], work[pivot]);
                    }

                    byte pivotVal = work[col][col];
                    if (pivotVal != 1)
                    {
                        byte inv = Galois.Inv(pivotVal);
                        for (int j = col; j < n * 2; j++)
                        {
                            work[col][j] = Galois.Mul(work[col][j], inv);
                        }
                    }

                    for (int row = 0; row < n; row++)
                    {
                        if (row == col) continue;
                        byte factor = work[row][col];
                        if (factor == 0) continue;

                        for (int j = col; j < n * 2; j++)
                        {
                            work[row][j] ^= Galois.Mul(factor, work[col][j]);
                        }
                    }
                }

                var result = new byte[n][];
                for (int r = 0; r < n; r++)
                {
                    result[r] = new byte[n];
                    Buffer.BlockCopy(work[r], n, result[r], 0, n);
                }

                return result;
            }
        }
    }
}

