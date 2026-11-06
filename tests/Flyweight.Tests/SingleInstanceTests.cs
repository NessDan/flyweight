using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Flyweight.Core;
using Xunit;

namespace Flyweight.Tests
{
    public class SingleInstanceTests
    {
        private static string NewId() => "fw-test-" + Guid.NewGuid().ToString("N");

        [Fact]
        public void Only_the_first_instance_becomes_primary()
        {
            string id = NewId();
            using (var first = new SingleInstance(id))
            using (var second = new SingleInstance(id))
            {
                Assert.True(first.TryBecomePrimary());
                Assert.False(second.TryBecomePrimary());
            }
        }

        [Fact]
        public void A_new_instance_can_take_over_after_the_primary_closes()
        {
            string id = NewId();
            using (var first = new SingleInstance(id))
            {
                Assert.True(first.TryBecomePrimary());
            }
            using (var next = new SingleInstance(id))
            {
                Assert.True(next.TryBecomePrimary());
            }
        }

        [Fact]
        public async Task Closing_from_a_different_thread_still_lets_a_new_instance_take_over()
        {
            string id = NewId();
            var first = new SingleInstance(id);
            Assert.True(first.TryBecomePrimary());

            await Task.Run(() => first.Dispose());

            using (var next = new SingleInstance(id))
            {
                Assert.True(next.TryBecomePrimary());
            }
        }

        [Fact]
        public void Closing_just_after_a_message_arrives_never_throws()
        {
            // Happens for real when a right-click arrives just as the window closes itself. The bad
            // timing is narrow, so this runs many at once to get the threads to collide.
            Parallel.For(0, 3000, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
            {
                string id = NewId();
                using (var received = new ManualResetEventSlim())
                {
                    var primary = new SingleInstance(id);
                    primary.TryBecomePrimary();
                    primary.StartListening(m =>
                    {
                        if (m == "last") received.Set();
                    });

                    Assert.True(SingleInstance.Send(id, "first", 10000));
                    Assert.True(SingleInstance.Send(id, "last", 10000));
                    Assert.True(received.Wait(10000));

                    primary.Dispose();
                }
            });
        }

        [Fact]
        public void A_closed_instance_stops_listening()
        {
            string id = NewId();
            var primary = new SingleInstance(id);
            primary.TryBecomePrimary();
            primary.StartListening(_ => { });
            Assert.True(SingleInstance.Send(id, "hello", 3000));

            primary.Dispose();
            Thread.Sleep(200);

            Assert.False(SingleInstance.Send(id, "anyone there?", 300));
        }

        [Fact]
        public void A_message_reaches_the_primary()
        {
            string id = NewId();
            using (var primary = new SingleInstance(id))
            using (var received = new ManualResetEventSlim())
            {
                string message = null;
                primary.TryBecomePrimary();
                primary.StartListening(m => { message = m; received.Set(); });

                Assert.True(SingleInstance.Send(id, "level=Light\nC:\\v\\動画.mp4", 3000));
                Assert.True(received.Wait(3000));
                Assert.Equal("level=Light\nC:\\v\\動画.mp4", message);
            }
        }

        [Fact]
        public async Task Selecting_many_files_at_once_loses_none_of_them()
        {
            // Explorer starts one process per selected file, all at the same moment.
            string id = NewId();
            using (var primary = new SingleInstance(id))
            {
                var received = new ConcurrentBag<string>();
                primary.TryBecomePrimary();
                primary.StartListening(received.Add);

                var sends = Enumerable.Range(0, 25)
                    .Select(i => Task.Run(() => SingleInstance.Send(id, "file" + i, 10000)))
                    .ToArray();
                var delivered = await Task.WhenAll(sends);
                Assert.All(delivered, Assert.True);

                for (int i = 0; i < 100 && received.Count < 25; i++) await Task.Delay(50);
                Assert.Equal(25, received.Distinct().Count());
            }
        }

        [Fact]
        public void Sending_with_nobody_listening_gives_up()
        {
            Assert.False(SingleInstance.Send(NewId(), "hello", 200));
        }

        [Fact]
        public void A_listener_that_throws_keeps_listening()
        {
            string id = NewId();
            using (var primary = new SingleInstance(id))
            using (var received = new ManualResetEventSlim())
            {
                primary.TryBecomePrimary();
                primary.StartListening(m =>
                {
                    if (m == "bad") throw new InvalidOperationException("boom");
                    received.Set();
                });

                Assert.True(SingleInstance.Send(id, "bad", 3000));
                Assert.True(SingleInstance.Send(id, "good", 3000));
                Assert.True(received.Wait(3000));
            }
        }
    }
}
