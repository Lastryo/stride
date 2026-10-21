// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Stride.Core.Diagnostics;
using Xunit;

namespace Stride.Core.BuildEngine.Tests;

public class TestBuildStepCompletion
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WaitersCompleteEvenWhenNotificationThrows(bool notificationThrows)
    {
        var directory = Path.Combine(Path.GetTempPath(), "stride-step-completion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var logger = new LoggerResult();
        using var builder = new Builder(logger, directory, null) { ThreadCount = 2 };
        var step = new CompletedStep();
        if (notificationThrows)
            step.StepProcessed += (_, _) => throw new InvalidOperationException("Notification failure");
        var before = Enumerable.Range(0, 32).Select(_ => step.ExecutedAsync()).ToArray();
        builder.Root.Add(step);
        var build = Task.Run(() => builder.Run(Builder.Mode.Build, false));
        try
        {
            var during = Enumerable.Range(0, 32).Select(_ => Task.Run(() => step.ExecutedAsync())).ToArray();
            await Task.WhenAll(before.Concat(during)).WaitAsync(TimeSpan.FromSeconds(10));
            await build.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(step, await step.ExecutedAsync());
            Assert.Equal(ResultStatus.Successful, step.Status);
            Assert.Equal(notificationThrows, logger.HasErrors);
        }
        finally
        {
            builder.CancelBuild();
            if (build.IsCompleted) Directory.Delete(directory, true);
        }
    }

    private sealed class CompletedStep : BuildStep
    {
        public override string Title => "Completion test";
        public override string ToString() => Title;
        public override Task<ResultStatus> Execute(IExecuteContext executeContext, BuilderContext builderContext)
            => Task.FromResult(ResultStatus.Successful);
    }
}
