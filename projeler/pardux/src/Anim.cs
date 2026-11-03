using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Pardux
{
    /// <summary>
    /// Gerçek yay fiziği (sönümlü harmonik hareket). damping &lt; 1 hafif taşma ile oturur, damping = 1 taşmadan yumuşakça durur.
    /// </summary>
    public class SpringEase : EasingFunctionBase
    {
        readonly double _z, _w;

        public SpringEase() : this(0.75, 11) { }

        public SpringEase(double damping, double stiffness)
        {
            _z = damping;
            _w = stiffness;
            EasingMode = EasingMode.EaseIn; // eğri doğrudan EaseInCore'dan gelsin
        }

        protected override double EaseInCore(double t)
        {
            if (t >= 1) return 1;
            if (_z >= 1) return 1 - (1 + _w * t) * Math.Exp(-_w * t);
            double wd = _w * Math.Sqrt(1 - _z * _z);
            return 1 - Math.Exp(-_z * _w * t) * (Math.Cos(wd * t) + (_z * _w / wd) * Math.Sin(wd * t));
        }

        protected override Freezable CreateInstanceCore() { return new SpringEase(_z, _w); }
    }

    /// <summary>Ortak animasyon yardımcıları.</summary>
    internal static class Anim
    {
        /// <summary>Açılış: canlı, çok hafif taşmalı yay.</summary>
        public static IEasingFunction SpringOpen { get { return new SpringEase(0.74, 10.5); } }
        /// <summary>Kapanış / küçülme: taşmasız, yumuşak duruş.</summary>
        public static IEasingFunction SpringClose { get { return new SpringEase(1.0, 9.5); } }

        public static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction Spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };

        public static DoubleAnimation To(double to, int ms, IEasingFunction ease = null, int delay = 0)
        {
            return new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Out, BeginTime = TimeSpan.FromMilliseconds(delay) };
        }

        public static DoubleAnimation FromTo(double from, double to, int ms, IEasingFunction ease = null, int delay = 0)
        {
            return new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Out, BeginTime = TimeSpan.FromMilliseconds(delay), FillBehavior = FillBehavior.HoldEnd };
        }

        /// <summary>Öğeyi aşağıdan hafifçe kayarak ve belirerek getirir.</summary>
        public static void Enter(UIElement e, int delay = 0, double dy = 8, int ms = 260)
        {
            var tt = new TranslateTransform(0, dy);
            e.RenderTransform = tt;
            e.Opacity = 0;
            e.BeginAnimation(UIElement.OpacityProperty, FromTo(0, 1, ms, null, delay));
            tt.BeginAnimation(TranslateTransform.YProperty, FromTo(dy, 0, ms + 60, Out, delay));
        }

        /// <summary>Hafif büyüyerek beliren "pop" efekti.</summary>
        public static void Pop(UIElement e, int delay = 0)
        {
            var st = new ScaleTransform(0.92, 0.92);
            e.RenderTransformOrigin = new Point(0.5, 0.5);
            e.RenderTransform = st;
            e.Opacity = 0;
            e.BeginAnimation(UIElement.OpacityProperty, FromTo(0, 1, 200, null, delay));
            st.BeginAnimation(ScaleTransform.ScaleXProperty, FromTo(0.92, 1, 320, Spring, delay));
            st.BeginAnimation(ScaleTransform.ScaleYProperty, FromTo(0.92, 1, 320, Spring, delay));
        }

        public static void Forever(Animatable target, DependencyProperty p, double from, double to, int ms, bool autoReverse, int delay = 0)
        {
            target.BeginAnimation(p, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = autoReverse,
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = autoReverse ? new SineEase { EasingMode = EasingMode.EaseInOut } : null
            });
        }

        public static void Forever(UIElement target, DependencyProperty p, double from, double to, int ms, bool autoReverse, int delay = 0)
        {
            target.BeginAnimation(p, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(ms))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = autoReverse,
                BeginTime = TimeSpan.FromMilliseconds(delay),
                EasingFunction = autoReverse ? new SineEase { EasingMode = EasingMode.EaseInOut } : null
            });
        }

        public static void Stop(Animatable target, DependencyProperty p, double value)
        {
            target.BeginAnimation(p, null);
            target.SetValue(p, value);
        }

        public static void Stop(UIElement target, DependencyProperty p, double value)
        {
            target.BeginAnimation(p, null);
            target.SetValue(p, value);
        }
    }
}
