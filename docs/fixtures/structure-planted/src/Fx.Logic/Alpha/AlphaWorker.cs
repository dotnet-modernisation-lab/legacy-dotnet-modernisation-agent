namespace Fx.Logic
{
    public class AlphaWorker
    {
        private readonly BetaWorker beta = new BetaWorker();
        public void Run(int id) { if (id > 0) { beta.Run(id - 1); } }
    }
}
