using Unity.Collections;
using Unity.Mathematics;
using System;

namespace GameWorld.ECS
{
    /// <summary>
    /// 重力索引 ( Gravity Index)
    /// 通过最小堆（Min-Heap）确保永远优先填充物理内存中最靠前的位置,有效避免内存碎片。
    /// </summary>
    public struct CompactingIndexAllocator : IDisposable
    {
        // 存储已回收索引的最小堆
        private NativeList<int> _freeHeap;

        // 当前索引的最高位
        private NativeReference<int> _highIndexMark;

        // 活跃掩码
        private NativeBitArray _activeMask;

        public CompactingIndexAllocator(int maxCapacity, Allocator allocator)
        {
            _freeHeap = new NativeList<int>(maxCapacity, allocator);
            _highIndexMark = new NativeReference<int>(0, allocator);
            _activeMask = new NativeBitArray(maxCapacity, allocator, NativeArrayOptions.ClearMemory);
        }

        /// <summary>
        /// 唤醒一个物理索引
        /// 注意，不要在并行逻辑中调用此函数
        /// </summary>
        public int Spawn()
        {
            while (_freeHeap.IsCreated && _freeHeap.Length > 0)
            {
                int minIndex = PopMin();
                
                // 惰性删除：移除掉高位的无效索引
                if (minIndex < _highIndexMark.Value)
                {
                    _activeMask.Set(minIndex, true);
                    return minIndex;
                }
            }

            int newIndex = _highIndexMark.Value++;
            _activeMask.Set(newIndex, true);
            return newIndex;
        }

        /// <summary>
        /// 回收一个物理索引
        /// 注意，不要在并行逻辑中调用此函数
        /// </summary>
        public void Despawn(int index)
        {
            // 首先检查索引合法性。
            if (!_activeMask.IsSet(index))
            {
                // 抛出异常，向顶层汇报这个逻辑错误。
                // 这种检查必须在任何条件判断之前，确保双重释放总是被捕获。
                throw new InvalidOperationException($"Double Free Detected or Invalid Index! Index {index} is not active.");
            }
            
       
            _activeMask.Set(index, false);

            // 尝试退回最高索引位置或加入空闲堆。
            // 判断是否是当前的边缘索引。
            // 注意：这里需要使用 Despawn 前的 _highWaterMark.Value 进行判断。
            if (index == _highIndexMark.Value - 1)
            {
                _highIndexMark.Value--;
                // 连续嗅探：如果前面的坑位之前也已经空了，继续回缩最高索引线
                // 循环条件要检查 _highWaterMark.Value > 0，因为如果退到 0 就不能再检查 _highWaterMark.Value - 1 了。
                while (_highIndexMark.Value > 0 && !_activeMask.IsSet(_highIndexMark.Value - 1))
                {
                    _highIndexMark.Value--;
                }
                
                // tips： _freeHeap 中高于_highWaterMark 的无效索引将在 Spawn 中处理。
            }
            else if (index < _highIndexMark.Value)
            {
                // 不是边缘，进入沉降
                PushHeap(index);
            }
        }

        public int GetActiveTotalCapacity() => _highIndexMark.Value;
        public int GetFreeCount() => _freeHeap.Length;

        #region 内部堆算法 (Min-Heap Logic)

        private void PushHeap(int index)
        {
            _freeHeap.Add(index);
            int childIdx = _freeHeap.Length - 1;

            while (childIdx > 0)
            {
                int parentIdx = (childIdx - 1) >> 1;
                if (_freeHeap[childIdx] >= _freeHeap[parentIdx]) break;

                // 交换：小的索引向上浮动
                int temp = _freeHeap[childIdx];
                _freeHeap[childIdx] = _freeHeap[parentIdx];
                _freeHeap[parentIdx] = temp;
                childIdx = parentIdx;
            }
        }

        private int PopMin()
        {
            int minIndex = _freeHeap[0];
            int lastIdx = _freeHeap.Length - 1;

            _freeHeap[0] = _freeHeap[lastIdx];
            _freeHeap.RemoveAt(lastIdx);

            if (_freeHeap.Length > 0)
            {
                int parentIdx = 0;
                while (true)
                {
                    int leftChild = (parentIdx << 1) + 1;
                    int rightChild = (parentIdx << 1) + 2;
                    if (leftChild >= _freeHeap.Length) break;

                    int smallestChild = (rightChild < _freeHeap.Length && _freeHeap[rightChild] < _freeHeap[leftChild])
                                        ? rightChild : leftChild;

                    if (_freeHeap[parentIdx] <= _freeHeap[smallestChild]) break;

                    int temp = _freeHeap[parentIdx];
                    _freeHeap[parentIdx] = _freeHeap[smallestChild];
                    _freeHeap[smallestChild] = temp;
                    parentIdx = smallestChild;
                }
            }

            return minIndex;
        }

        #endregion

        public void Dispose()
        {
            if (_freeHeap.IsCreated) _freeHeap.Dispose();
            if (_highIndexMark.IsCreated) _highIndexMark.Dispose();
            if (_activeMask.IsCreated) _activeMask.Dispose();
        }
    }
}
