// The weigh-in. Swings the scale to whichever level someone picks. The page is complete without this,
// since the HTML already shows the Balanced result.
(function () {
  var needle = document.getElementById('needle');
  var value = document.getElementById('dial-value');
  var result = document.getElementById('weighin-result');
  var levels = Array.prototype.slice.call(document.querySelectorAll('.level'));
  var calm = matchMedia('(prefers-reduced-motion: reduce)').matches;
  var shown = parseFloat(value.textContent);
  var frame = 0;

  function weighIn(weight) {
    needle.style.setProperty('--weight', weight);
    cancelAnimationFrame(frame);

    if (calm) {
      shown = weight;
      value.textContent = weight.toFixed(1);
      return;
    }

    var from = shown;
    var started = performance.now();
    function step(now) {
      // A frame's timestamp can be slightly earlier than the moment this started.
      var progress = Math.max(0, Math.min((now - started) / 800, 1));
      shown = from + (weight - from) * (1 - Math.pow(1 - progress, 3));
      value.textContent = shown.toFixed(1);
      if (progress < 1) frame = requestAnimationFrame(step);
    }
    frame = requestAnimationFrame(step);
  }

  function choose(level) {
    levels.forEach(function (other) {
      other.setAttribute('aria-pressed', other === level ? 'true' : 'false');
    });
    weighIn(parseFloat(level.dataset.mb));
    result.innerHTML = '<strong>' + level.dataset.smaller + '% smaller.</strong> Picture score ' + level.dataset.score + ' out of 100.';
  }

  levels.forEach(function (level) {
    level.addEventListener('click', function () { choose(level); });
  });

  // The page opened with the needle at the starting weight. Now it drops.
  if (!calm) {
    setTimeout(function () {
      var picked = levels.filter(function (level) { return level.getAttribute('aria-pressed') === 'true'; })[0];
      choose(picked);
    }, 650);
  }
})();
