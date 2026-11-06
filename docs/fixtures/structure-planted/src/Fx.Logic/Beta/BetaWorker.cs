namespace Fx.Logic
{
    public class BetaWorker
    {
        private readonly AlphaWorker alpha = new AlphaWorker();
        public void Run(int id) { if (id > 0) { alpha.Run(id - 1); } }
    }
}
