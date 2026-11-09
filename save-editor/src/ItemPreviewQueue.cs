using System;
using System.Threading;
using System.Threading.Tasks;

namespace SealSaveEditor
{
    internal sealed class ItemPreviewQueue
    {
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        public async Task<ItemInitialState> Run(Func<ItemInitialState> create, Func<bool> stillWanted)
        {
            await gate.WaitAsync();
            try
            {
                if (!stillWanted()) return null;
                return await Task.Run(create);
            }
            finally { gate.Release(); }
        }
    }
}
