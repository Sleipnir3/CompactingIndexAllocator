# CompactingIndexAllocator

`CompactingIndexAllocator` 是一个专为 Unity DOTS 设计的**高性能、紧凑型物理索引分配器**。它并非传统的对象池，而是通过算法维持整数索引的最密排布，使活跃数据在物理内存中如受重力般“沉降”在最低地址，从而最大化 CPU Cache Line 命中率。

## ✨ 核心机制

* **重力沉降 (Min-Heap Allocation)**
  永远优先复用数值最小的空闲索引，确保内存前段被 100% 填满。
* **连环退潮 (Watermark Retreat)**
  当高水位的边缘实体被销毁时，系统会像多米诺骨牌一样向回坍缩，瞬间收复尾部所有内存空洞。
* **O(1) 安全防线**
  内置 `NativeBitArray` 作为考勤表，以极低开销绝对拦截“双重释放 (Double Free)”灾难。
* **惰性清理 (Lazy Deletion)**
  退潮时越过的僵尸索引不会被立即从堆中剔除，而是在下次 `Spawn` 弹出时被 O(1) 丢弃，彻底消除碎片整理开销。

## ⚠️ 架构规约

**严禁并发调用**：
为了避免破坏堆结构与引发缓存一致性风暴，`Spawn()` 和 `Despawn()` **禁止**在 `IJobParallelFor` 中直接调用。

**正确使用姿势**：
在并发 Job 中仅记录指令（如存入无锁队列），随后在主线程的**同步点 (Sync Point)** 集中、串行地调用本池。
