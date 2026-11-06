using System;
using System.ComponentModel;
using System.IO;
using System.Threading;
using Flyweight.Core;

namespace Flyweight.UI
{
    /// <summary>One row in the queue: what to show for a video as it moves through compression.</summary>
    public sealed class JobViewModel : INotifyPropertyChanged
    {
        private const double FinishingPercent = 99.5;

        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private string _status = "Waiting…";
        private double _percent;
        private bool _isRunning;
        private bool _isFinished;
        private bool _failed;
        private bool _cancelRequested;
        private string _resultPath;
        private string _details;

        public JobViewModel(string path, LevelSettings settings)
        {
            InputPath = path;
            Settings = settings;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string InputPath { get; }
        public LevelSettings Settings { get; }
        public string FileName => Path.GetFileName(InputPath);
        public CancellationToken CancelToken => _cancel.Token;

        public string Status { get => _status; private set => Set(ref _status, value, nameof(Status)); }
        public double Percent { get => _percent; private set => Set(ref _percent, value, nameof(Percent)); }
        public bool IsRunning { get => _isRunning; private set => Set(ref _isRunning, value, nameof(IsRunning)); }
        public bool IsFinished { get => _isFinished; private set => Set(ref _isFinished, value, nameof(IsFinished)); }
        public bool Failed { get => _failed; private set => Set(ref _failed, value, nameof(Failed)); }
        public string ResultPath { get => _resultPath; private set => Set(ref _resultPath, value, nameof(ResultPath)); }
        public string Details { get => _details; private set => Set(ref _details, value, nameof(Details)); }

        public bool CanCancel => !IsFinished && !_cancelRequested;
        public bool CanShowFile => ResultPath != null;
        public bool HasDetails => !string.IsNullOrEmpty(Details);

        public void MarkStarted()
        {
            IsRunning = true;
            Status = "Starting…";
            Notify(nameof(CanCancel));
        }

        public void Apply(CompressionProgress progress)
        {
            if (_cancelRequested || IsFinished) return;

            Percent = progress.Percent;

            string state;
            if (progress.Percent >= FinishingPercent)
            {
                state = "Finishing…";
            }
            else
            {
                string percent = Math.Round(progress.Percent) + "%";
                state = progress.Remaining.HasValue ? $"{percent} · {Format.Clock(progress.Remaining.Value)} left" : percent;
            }
            Status = progress.Note == null ? state : $"{progress.Note} · {state}";
        }

        public void Apply(CompressionResult result)
        {
            switch (result.Outcome)
            {
                case JobOutcome.Done:
                    Percent = 100;
                    ResultPath = result.OutputPath;
                    string savings = Format.Savings(result.InputBytes, result.OutputBytes);
                    Status = string.IsNullOrEmpty(result.Message) ? savings : $"{savings} · {result.Message}";
                    break;
                case JobOutcome.AlreadySmall:
                    ResultPath = InputPath;
                    Status = result.Message;
                    break;
                case JobOutcome.Canceled:
                    Status = "Cancelled";
                    break;
                default:
                    Failed = true;
                    Details = result.Details;
                    Status = result.Message;
                    break;
            }

            Finish();
        }

        public void Cancel()
        {
            if (!CanCancel) return;

            _cancelRequested = true;
            _cancel.Cancel();

            if (IsRunning)
            {
                Status = "Cancelling…";
                Notify(nameof(CanCancel));
            }
            else
            {
                Status = "Cancelled";
                Finish();
            }
        }

        private void Finish()
        {
            IsRunning = false;
            IsFinished = true;
            Notify(nameof(CanCancel));
            Notify(nameof(CanShowFile));
            Notify(nameof(HasDetails));
        }

        private void Set<T>(ref T field, T value, string name)
        {
            if (Equals(field, value)) return;
            field = value;
            Notify(name);
        }

        private void Notify(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
