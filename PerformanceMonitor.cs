using System.Diagnostics;
public class PerformanceMonitor
{
    private bool firstRun = true;

    public long AverageRunTime { get; private set; }
    public long MaxRunTime { get; private set; }
    public string Name { get; }

    private int lastIndex = 0;

    public long[] LastFewRuns { get; } = new long[25];
    public string LastIterations => $"{this.Name}\t{this.LastFewRuns.Min().ToString("00")}/{this.LastFewRuns.Average().ToString("00")}/{this.LastFewRuns.Max().ToString("00")} | {string.Join(" ", this.LastFewRuns.Select(x => x.ToString("00")).ToList())}";

    public string LastIterationsShort => $"{this.LastFewRuns.Min().ToString("00")} {this.LastFewRuns.Average().ToString("00")} {this.LastFewRuns.Max().ToString("00")} | {string.Join(" ", this.LastFewRuns.Select(x => x.ToString("00")).ToList())}";


    private Stopwatch watch = new Stopwatch();

    public PerformanceMonitor(string name)
    {
        this.Name = name;
    }

    public void Restart()
    {
        this.watch.Restart();
    }

    public void Stop()
    {
        this.watch.Stop();

        this.LastFewRuns[this.lastIndex] = this.watch.ElapsedMilliseconds;
        this.lastIndex += 1;
        this.lastIndex %= 25;

        if (!this.firstRun)
        {
            this.MaxRunTime = this.watch.ElapsedMilliseconds;
            this.AverageRunTime = this.watch.ElapsedMilliseconds;

            this.firstRun = true;
        }
        else
        {
            if (this.watch.ElapsedMilliseconds > this.MaxRunTime)
            {
                this.MaxRunTime += this.watch.ElapsedMilliseconds;
            }

            this.AverageRunTime = (this.AverageRunTime + this.watch.ElapsedMilliseconds) / 2;
        }
    }

    public void ResetMaxRunTime()
    {
        this.MaxRunTime = this.AverageRunTime;
    }
}
