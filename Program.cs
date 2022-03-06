using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using OpenQA.Selenium.Support.UI;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium;
using System.Drawing;
using System.Linq;
using System.IO;
using System;

namespace FastFloodCheater
{
    public class Program
    {
        public static readonly int GRID_WIDTH = 10;
        public static readonly string SITE_URL = "https://fastflood.dylancastillo.co/";
        public static readonly string PLAY_NOW_BUTTON_ID = "start-btn";
        public static readonly string COLOR_BUTTON_CLASS = "color-button";
        public static readonly string CELL_CLASS = "cell";

        public static void Main(string[] args)
        {
            IWebDriver driver = new ChromeDriver(Directory.GetCurrentDirectory());
            driver.Url = SITE_URL;
            var timeout = 10000;
            var wait = new WebDriverWait(driver, TimeSpan.FromMilliseconds(timeout));
            wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").Equals("complete"));
            driver.FindElement(By.Id(PLAY_NOW_BUTTON_ID)).Click();
            var cells = driver.FindElements(By.ClassName(CELL_CLASS));
            var grid = MakeGrid(cells);

            var numSolutions = 10_000;
            int minSteps = int.MaxValue;
            object sync = new object();
            var solutions = new ConcurrentBag<List<Color>>();
            Parallel.ForEach(Enumerable.Range(0, numSolutions), _ =>
            {
                solutions.Add(GetSolution(grid, ref minSteps, sync));
            });

            var allSolutions = solutions.Where(s => s.Count <= minSteps + 1).ToList();
            var bestSolution = allSolutions.First();
            //var optimizedSolution = GetOptimalSolution(allSolutions);//not good
            for (int i = 0; i < minSteps + 1; i++)
            {
                string step = "";
                foreach (var solution in allSolutions)
                {
                    step = $"{step} {(solution.Count > i ? solution[i].ToString() : "Done"),7}";
                }
                Console.WriteLine($"{step}");
            }
            InputSolution(driver, bestSolution);
            Console.WriteLine($"Press any key to exit.");
            Console.ReadLine();
        }

        #region Color Stuff
        public enum Color
        {
            Red,
            Blue,
            Orange,
            Yellow,
            Green,
            Purple
        }

        public static Tuple<Color, IWebElement> GetColorTuple(string unicode, IWebElement element) => new Tuple<Color, IWebElement>(GetColor(unicode), element);

        public static Color GetColor(string unicode) =>
            unicode switch
            {
                "\U0001f7e5" => Color.Red,
                "\U0001f7e6" => Color.Blue,
                "\U0001f7e7" => Color.Orange,
                "\U0001f7e8" => Color.Yellow,
                "\U0001f7e9" => Color.Green,
                "\U0001f7ea" => Color.Purple,
                _ => throw new ArgumentException("Invalid unicode value", unicode)
            };

        public static Color GetRandomColor(Color previousColor)
        {
            var rand = new Random();
            List<Color> colors = Enum.GetValues(typeof(Color)).OfType<Color>().Where(color => color != previousColor).ToList();
            return colors[rand.Next(colors.Count)];
        }
        #endregion Color Stuff

        #region Grid Stuff
        public static List<List<Tuple<Color, IWebElement>>> CopyGrid(List<List<Tuple<Color, IWebElement>>> grid)
        {
            var newGrid = new List<List<Tuple<Color, IWebElement>>>();
            foreach (var row in grid)
            {
                var newRow = new List<Tuple<Color, IWebElement>>();
                foreach (var cell in row)
                {
                    newRow.Add(new Tuple<Color, IWebElement>(cell.Item1, cell.Item2));
                }
                newGrid.Add(newRow);
            }
            return newGrid;
        }

        public static List<List<Tuple<Color, IWebElement>>> MakeGrid(ReadOnlyCollection<IWebElement> cells)
        {
            var grid = new List<List<Tuple<Color, IWebElement>>>();
            var width = GRID_WIDTH;
            var row = new List<Tuple<Color, IWebElement>>();
            foreach (var cell in cells)
            {
                var color = GetColorTuple(cell.Text, cell);
                row.Add(color);
                if (--width == 0)
                {
                    width = GRID_WIDTH;
                    grid.Add(row);
                    row = new List<Tuple<Color, IWebElement>>();
                }
            }
            return grid;
        }

        public static void FillGrid(ref List<List<Tuple<Color, IWebElement>>> grid, Color color)
        {
            Color previousColor = grid[0][0].Item1;
            if (previousColor == color) return;
            Stack<Point> points = new Stack<Point>();

            points.Push(new Point(0, 0));
            while (points.Count > 0)
            {
                Point a = points.Pop();
                if (a.X < grid.First().Count && a.X >= 0 && a.Y < grid.Count() && a.Y >= 0)
                {
                    var item = grid[a.Y][a.X];
                    bool isPreviousColor = previousColor == item.Item1;
                    if (isPreviousColor)
                    {
                        var tempElement = grid[a.Y][a.X].Item2;
                        grid[a.Y][a.X] = new Tuple<Color, IWebElement>(color, tempElement);
                        points.Push(new Point(a.X - 1, a.Y));
                        points.Push(new Point(a.X + 1, a.Y));
                        points.Push(new Point(a.X, a.Y - 1));
                        points.Push(new Point(a.X, a.Y + 1));
                    }
                }
            }
        }

        public static bool IsComplete(List<List<Tuple<Color, IWebElement>>> grid)
        {
            return grid.All(row =>
            {
                var color = row.First().Item1;
                return row.All(cell => cell.Item1 == color);
            });
        }
        #endregion Grid Stuff

        public static List<Color> GetSolution(List<List<Tuple<Color, IWebElement>>> grid, ref int minSteps, object sync)
        {
            var gridCopy = CopyGrid(grid);
            var solution = new List<Color>();
            var previousColor = gridCopy.First().First().Item1;
            int steps = 0;
            bool completed = false;
            while (steps++ <= minSteps)
            {
                completed = IsComplete(gridCopy);
                if (completed)
                    break;
                var currentColor = GetRandomColor(previousColor);
                solution.Add(currentColor);
                FillGrid(ref gridCopy, currentColor);
                previousColor = currentColor;
            }
            if (completed)
                lock (sync) minSteps = solution.Count;
            else
                solution.Add(solution.Last());
            return solution;
        }

        public static void InputSolution(IWebDriver driver, List<Color> solution)
        {
            var buttonElements = driver.FindElements(By.ClassName(COLOR_BUTTON_CLASS));
            var buttons = new List<Tuple<Color, IWebElement>>();
            buttonElements.ToList().ForEach(we =>
            {
                buttons.Add(GetColorTuple(we.Text, we));
            });
            foreach (Color step in solution)
            {
                buttons.Where(b => b.Item1 == step).First().Item2.Click();
            }
        }

        public static List<Color> GetOptimalSolution(List<List<Color>> solutions)
        {
            var optimalSolution = new List<Color>();
            for (int i = 0; i < solutions[0].Count; i++)
            {
                Dictionary<Color, int> options = new Dictionary<Color, int>();
                solutions.ForEach(s =>
                {
                    var color = s[i];
                    if (options.ContainsKey(color))
                    {
                        options[color] += 1;
                    }
                    else
                    {
                        options[color] = 1;
                    }
                });
                var bestOption = options.Where(o => o.Value == options.Max(kvp => kvp.Value)).First();
                optimalSolution.Add(bestOption.Key);
            }
            return optimalSolution;
        }
    }
}
