using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;
using OpenCvSharp.Features2D;

namespace DocumentComparisonFinal
{
    class Program
    {
        static void Main(string[] args)
        {
            using var matPhone = Cv2.ImRead("test.jpg");
            using var matPdf = Cv2.ImRead("test123.png");
            if (matPhone.Empty() || matPdf.Empty())
            {
                Console.WriteLine("Не удалось прочитать test.jpg или test123.png.");
                return;
            }

            var outputDir = Path.Combine(AppContext.BaseDirectory, "output");
            Directory.CreateDirectory(outputDir);

            // 1. Точное выравнивание
            var homography = GetPrecisionHomography(matPhone, matPdf);
            using var aligned = new Mat();
            Cv2.WarpPerspective(matPhone, aligned, homography, matPdf.Size(), InterpolationFlags.Lanczos4);
            var alignedPath = Path.Combine(outputDir, "aligned_document.png");
            Cv2.ImWrite(alignedPath, aligned);

            // 2. Бинаризация
            using var binPhone = PrepareForComparison(aligned);
            using var binPdf = PrepareForComparison(matPdf);

            // 3. СОЗДАНИЕ УМНОЙ МАСКИ С УЛУЧШЕННОЙ ЗАЩИТОЙ
            using var pdfNot = new Mat();
            Cv2.BitwiseNot(binPdf, pdfNot);

            // --- ШАГ А: Защищаем только реальные чекбоксы ---
            using var labelsPdf = new Mat();
            using var statsPdf = new Mat();
            using var centroidsPdf = new Mat();
            int nLabelsPdf = Cv2.ConnectedComponentsWithStats(pdfNot, labelsPdf, statsPdf, centroidsPdf);

            using var protectMask = new Mat(matPdf.Size(), MatType.CV_8UC1, Scalar.All(0));
            using var checkboxContentMask = new Mat(matPdf.Size(), MatType.CV_8UC1, Scalar.All(0));
            using var checkboxInnerMask = new Mat(matPdf.Size(), MatType.CV_8UC1, Scalar.All(0));
            using var checkboxOutlineMask = new Mat(matPdf.Size(), MatType.CV_8UC1, Scalar.All(0));
            var pdfStatsIndexer = statsPdf.GetGenericIndexer<int>();
            var checkboxInnerRects = new List<Rect>();
            var checkboxValidationRects = new List<Rect>();
            var checkboxContentRects = new List<Rect>();

            for (int i = 1; i < nLabelsPdf; i++)
            {
                int area = pdfStatsIndexer[i, (int)ConnectedComponentsTypes.Area];
                int left = pdfStatsIndexer[i, (int)ConnectedComponentsTypes.Left];
                int top = pdfStatsIndexer[i, (int)ConnectedComponentsTypes.Top];
                int w = pdfStatsIndexer[i, (int)ConnectedComponentsTypes.Width];
                int h = pdfStatsIndexer[i, (int)ConnectedComponentsTypes.Height];
                double aspect = (double)h / w;

                // СТРОГИЙ ФИЛЬТР: Квадратная форма (чекбокс) и малый размер
                // Это исключит длинные линии из защиты
                if (area < 400 && w < 35 && h < 35 && aspect > 0.7 && aspect < 1.3)
                {
                    using var component = new Mat();
                    Cv2.Compare(labelsPdf, i, component, CmpTypes.EQ);
                    if (!LooksLikeCheckbox(component, new Rect(left, top, w, h)))
                    {
                        continue;
                    }

                    Cv2.BitwiseOr(checkboxOutlineMask, component, checkboxOutlineMask);

                    const int checkboxMargin = 3;
                    int x1 = Math.Max(0, left - checkboxMargin);
                    int y1 = Math.Max(0, top - checkboxMargin);
                    int x2 = Math.Min(matPdf.Width, left + w + checkboxMargin);
                    int y2 = Math.Min(matPdf.Height, top + h + checkboxMargin);
                    Cv2.Rectangle(protectMask, new Rect(x1, y1, x2 - x1, y2 - y1), Scalar.All(255), -1);

                    const int checkboxContentMargin = 3;
                    int contentX1 = Math.Max(0, left - checkboxContentMargin);
                    int contentY1 = Math.Max(0, top - checkboxContentMargin);
                    int contentX2 = Math.Min(matPdf.Width, left + w + checkboxContentMargin);
                    int contentY2 = Math.Min(matPdf.Height, top + h + checkboxContentMargin);
                    var contentRect = new Rect(contentX1, contentY1, contentX2 - contentX1, contentY2 - contentY1);
                    checkboxContentRects.Add(contentRect);
                    Cv2.Rectangle(checkboxContentMask, contentRect, Scalar.All(255), -1);

                    const int checkboxBorder = 1;
                    int innerX = left + checkboxBorder;
                    int innerY = top + checkboxBorder;
                    int innerW = w - checkboxBorder * 2;
                    int innerH = h - checkboxBorder * 2;
                    if (innerW > 0 && innerH > 0)
                    {
                        var innerRect = new Rect(innerX, innerY, innerW, innerH);
                        checkboxInnerRects.Add(innerRect);
                        Cv2.Rectangle(checkboxInnerMask, innerRect, Scalar.All(255), -1);

                        int minInnerSize = Math.Min(innerW, innerH);
                        int validationBorder = minInnerSize >= 7 ? 2 : minInnerSize >= 5 ? 1 : 0;
                        var validationRect = new Rect(
                            innerX + validationBorder,
                            innerY + validationBorder,
                            innerW - validationBorder * 2,
                            innerH - validationBorder * 2);
                        checkboxValidationRects.Add(validationRect);
                    }
                }
            }

            Cv2.Dilate(checkboxOutlineMask, checkboxOutlineMask, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));

            // --- ШАГ Б: Маски для удаления ---
            using var templateInkMask = new Mat();
            Cv2.Dilate(pdfNot, templateInkMask, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)));
            using var textMask = templateInkMask.Clone();

            using var checkboxTemplateInkMask = new Mat();
            Cv2.Dilate(pdfNot, checkboxTemplateInkMask, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 7)));

            using var lineMask = new Mat();
            using var vEx = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, 30));
            using var hEx = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(30, 1));
            using var vM = new Mat(); Cv2.Erode(pdfNot, vM, vEx); Cv2.Dilate(vM, vM, vEx);
            using var hM = new Mat(); Cv2.Erode(pdfNot, hM, hEx); Cv2.Dilate(hM, hM, hEx);
            Cv2.BitwiseOr(vM, hM, lineMask);
            Cv2.Dilate(lineMask, lineMask, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(8, 8))); // Максимальная зачистка линий

            // --- ШАГ В: Применяем защиту ---
            Cv2.BitwiseAnd(textMask, textMask, textMask, mask: ~protectMask);
            Cv2.BitwiseAnd(lineMask, lineMask, lineMask, mask: ~protectMask);

            // 4. ИЗВЛЕЧЕНИЕ
            using var photoText = new Mat();
            Cv2.BitwiseNot(binPhone, photoText);

            using var cleanedDiff = new Mat();
            using var noLines = new Mat();
            Cv2.BitwiseAnd(photoText, photoText, noLines, mask: ~lineMask);
            Cv2.BitwiseAnd(noLines, noLines, cleanedDiff, mask: ~textMask);
            Cv2.BitwiseAnd(cleanedDiff, cleanedDiff, cleanedDiff, mask: ~checkboxOutlineMask);

            // 5. ГЕОМЕТРИЧЕСКИЙ КИЛЛЕР ЛИНИЙ (Если что-то выжило)
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int nLabels = Cv2.ConnectedComponentsWithStats(cleanedDiff, labels, stats, centroids);

            using var finalMask = new Mat(matPdf.Size(), MatType.CV_8UC1, Scalar.All(0));
            var statsIndexer = stats.GetGenericIndexer<int>();

            for (int i = 1; i < nLabels; i++)
            {
                int area = statsIndexer[i, (int)ConnectedComponentsTypes.Area];
                int w = statsIndexer[i, (int)ConnectedComponentsTypes.Width];
                int h = statsIndexer[i, (int)ConnectedComponentsTypes.Height];
                double aspect = (double)h / w;

                // УБИВАЕМ ЛЮБЫЕ ОСТАТКИ ЛИНИЙ:
                // Если объект очень длинный/высокий при малой ширине/высоте - это мусорная линия
                bool isStillALine = (aspect > 3.5 && w < 8) || (aspect < 0.28 && h < 8);
                bool isSmallDust = area < 10;

                using var componentMask = new Mat();
                Cv2.Compare(labels, i, componentMask, CmpTypes.EQ);

                using var protectedPart = new Mat();
                Cv2.BitwiseAnd(componentMask, checkboxInnerMask, protectedPart);
                bool isInCheckbox = Cv2.CountNonZero(protectedPart) > 0;

                if (isInCheckbox || (!isStillALine && !isSmallDust))
                {
                    Cv2.BitwiseOr(finalMask, componentMask, finalMask);
                }
            }

            using var rawCheckboxContent = new Mat();
            Cv2.BitwiseAnd(photoText, photoText, rawCheckboxContent, mask: checkboxContentMask);
            Cv2.BitwiseAnd(rawCheckboxContent, rawCheckboxContent, rawCheckboxContent, mask: ~checkboxTemplateInkMask);

            using var checkboxContent = new Mat(matPdf.Size(), MatType.CV_8UC1, Scalar.All(0));
            for (int i = 0; i < checkboxContentRects.Count; i++)
            {
                var rect = checkboxContentRects[i];
                using var sourceRoi = new Mat(rawCheckboxContent, rect);
                using var filteredRoi = new Mat(sourceRoi.Size(), MatType.CV_8UC1, Scalar.All(0));
                CopyFilteredCheckboxComponents(sourceRoi, filteredRoi);

                var validationRect = checkboxValidationRects[i];
                var validationLocalRect = new Rect(
                    validationRect.X - rect.X,
                    validationRect.Y - rect.Y,
                    validationRect.Width,
                    validationRect.Height);

                using var validationRoi = new Mat(filteredRoi, validationLocalRect);
                if (!HasEnoughCheckboxInk(validationRoi))
                {
                    continue;
                }

                using var targetRoi = new Mat(checkboxContent, rect);
                filteredRoi.CopyTo(targetRoi);
            }

            Cv2.BitwiseAnd(finalMask, finalMask, finalMask, mask: ~protectMask);
            Cv2.Dilate(finalMask, finalMask, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2)));
            Cv2.BitwiseAnd(finalMask, finalMask, finalMask, mask: ~protectMask);
            Cv2.BitwiseOr(finalMask, checkboxContent, finalMask);
            RemoveSmallResidues(finalMask);

            // 6. НАЛОЖЕНИЕ
            using var resultOverlay = matPdf.Clone();
            resultOverlay.SetTo(new Scalar(0, 0, 255), finalMask);

            var maskPath = Path.Combine(outputDir, "difference_mask.png");
            var resultPath = Path.Combine(outputDir, "comparison_result.png");
            Cv2.ImWrite(maskPath, finalMask);
            Cv2.ImWrite(resultPath, resultOverlay);

            Console.WriteLine($"Выровненный документ: {alignedPath}");
            Console.WriteLine($"Маска отличий: {maskPath}");
            Console.WriteLine($"Итоговое изображение: {resultPath}");

            bool showWindow = !args.Any(arg => string.Equals(arg, "--no-window", StringComparison.OrdinalIgnoreCase));
            if (showWindow)
            {
                Cv2.ImShow("Final Clean Result", resultOverlay);
                Cv2.WaitKey(0);
            }
        }

        static Mat PrepareForComparison(Mat src)
        {
            using var gray = src.CvtColor(ColorConversionCodes.BGR2GRAY);
            var binary = new Mat();
            // Адаптивный порог с мягким параметром (10), чтобы не превращать шум в буквы
            Cv2.AdaptiveThreshold(gray, binary, 255, AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary, 15, 10);
            return binary;
        }

        static bool LooksLikeCheckbox(Mat componentMask, Rect bounds)
        {
            if (bounds.Width < 4 || bounds.Width > 18 || bounds.Height < 4 || bounds.Height > 18)
            {
                return false;
            }

            using var roi = new Mat(componentMask, bounds);
            using var top = roi.Row(0);
            using var bottom = roi.Row(bounds.Height - 1);
            using var left = roi.Col(0);
            using var right = roi.Col(bounds.Width - 1);

            bool hasHorizontalEdges =
                Cv2.CountNonZero(top) >= bounds.Width * 0.65 &&
                Cv2.CountNonZero(bottom) >= bounds.Width * 0.65;
            bool hasVerticalEdges =
                Cv2.CountNonZero(left) >= bounds.Height * 0.65 &&
                Cv2.CountNonZero(right) >= bounds.Height * 0.65;

            if (!hasHorizontalEdges || !hasVerticalEdges)
            {
                return false;
            }

            if (bounds.Width <= 4 || bounds.Height <= 4)
            {
                return true;
            }

            using var center = new Mat(roi, new Rect(1, 1, bounds.Width - 2, bounds.Height - 2));
            double centerFill = Cv2.CountNonZero(center) / (double)(center.Width * center.Height);
            return centerFill < 0.45;
        }

        static bool HasEnoughCheckboxInk(Mat roi)
        {
            int inkPixels = Cv2.CountNonZero(roi);
            int minInkPixels = Math.Min(8, Math.Max(3, roi.Width * roi.Height / 3));
            if (inkPixels < minInkPixels)
            {
                return false;
            }

            int rowsWithInk = 0;
            for (int y = 0; y < roi.Height; y++)
            {
                using var row = roi.Row(y);
                if (Cv2.CountNonZero(row) > 0)
                {
                    rowsWithInk++;
                }
            }

            int colsWithInk = 0;
            for (int x = 0; x < roi.Width; x++)
            {
                using var col = roi.Col(x);
                if (Cv2.CountNonZero(col) > 0)
                {
                    colsWithInk++;
                }
            }

            return rowsWithInk >= 2 && colsWithInk >= 2;
        }

        static void CopyFilteredCheckboxComponents(Mat sourceRoi, Mat targetRoi)
        {
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int nLabels = Cv2.ConnectedComponentsWithStats(sourceRoi, labels, stats, centroids);
            var statsIndexer = stats.GetGenericIndexer<int>();

            for (int i = 1; i < nLabels; i++)
            {
                int area = statsIndexer[i, (int)ConnectedComponentsTypes.Area];
                int left = statsIndexer[i, (int)ConnectedComponentsTypes.Left];
                int top = statsIndexer[i, (int)ConnectedComponentsTypes.Top];
                int w = statsIndexer[i, (int)ConnectedComponentsTypes.Width];
                int h = statsIndexer[i, (int)ConnectedComponentsTypes.Height];

                if (area < 2 || LooksLikeCheckboxResidue(labels, i, new Rect(left, top, w, h), area))
                {
                    continue;
                }

                using var component = new Mat();
                Cv2.Compare(labels, i, component, CmpTypes.EQ);
                Cv2.BitwiseOr(targetRoi, component, targetRoi);
            }
        }

        static bool LooksLikeCheckboxResidue(Mat labels, int label, Rect bounds, int area)
        {
            if (bounds.Width <= 2 || bounds.Height <= 2)
            {
                return true;
            }

            if (bounds.Width > 12 || bounds.Height > 12 || area > 30)
            {
                return false;
            }

            using var component = new Mat();
            Cv2.Compare(labels, label, component, CmpTypes.EQ);
            using var roi = new Mat(component, bounds);

            int edgeHits = 0;
            using (var top = roi.Row(0))
            using (var bottom = roi.Row(bounds.Height - 1))
            using (var left = roi.Col(0))
            using (var right = roi.Col(bounds.Width - 1))
            {
                if (Cv2.CountNonZero(top) > 0) edgeHits++;
                if (Cv2.CountNonZero(bottom) > 0) edgeHits++;
                if (Cv2.CountNonZero(left) > 0) edgeHits++;
                if (Cv2.CountNonZero(right) > 0) edgeHits++;
            }

            if (edgeHits < 4 || bounds.Width <= 2 || bounds.Height <= 2)
            {
                return false;
            }

            using var center = new Mat(roi, new Rect(1, 1, bounds.Width - 2, bounds.Height - 2));
            return Cv2.CountNonZero(center) == 0;
        }

        static void RemoveSmallResidues(Mat mask)
        {
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            int nLabels = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids);
            var statsIndexer = stats.GetGenericIndexer<int>();

            using var filtered = new Mat(mask.Size(), MatType.CV_8UC1, Scalar.All(0));
            for (int i = 1; i < nLabels; i++)
            {
                int area = statsIndexer[i, (int)ConnectedComponentsTypes.Area];
                int left = statsIndexer[i, (int)ConnectedComponentsTypes.Left];
                int top = statsIndexer[i, (int)ConnectedComponentsTypes.Top];
                int w = statsIndexer[i, (int)ConnectedComponentsTypes.Width];
                int h = statsIndexer[i, (int)ConnectedComponentsTypes.Height];

                bool isNarrowSpeck = area <= 20 && (w <= 3 || h <= 3);
                bool isTinyBoxNoise = area <= 30 && w <= 7 && h <= 8;
                if (area < 4 || isNarrowSpeck || isTinyBoxNoise || LooksLikeCheckboxResidue(labels, i, new Rect(left, top, w, h), area))
                {
                    continue;
                }

                using var component = new Mat();
                Cv2.Compare(labels, i, component, CmpTypes.EQ);
                Cv2.BitwiseOr(filtered, component, filtered);
            }

            filtered.CopyTo(mask);
        }

        static Mat GetPrecisionHomography(Mat src, Mat target)
        {
            using var gSrc = src.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var gTarget = target.CvtColor(ColorConversionCodes.BGR2GRAY);
            var sift = SIFT.Create();
            KeyPoint[] kp1, kp2;
            using var desc1 = new Mat(); using var desc2 = new Mat();
            sift.DetectAndCompute(gSrc, null, out kp1, desc1);
            sift.DetectAndCompute(gTarget, null, out kp2, desc2);
            var matcher = new BFMatcher(NormTypes.L2);
            var matches = matcher.KnnMatch(desc1, desc2, 2);
            var good = matches.Where(m => m[0].Distance < 0.7f * m[1].Distance).Select(m => m[0]).ToList();
            if (good.Count < 10) return Mat.Eye(3, 3, MatType.CV_64F);
            var srcPts = good.Select(m => kp1[m.QueryIdx].Pt).ToArray();
            var dstPts = good.Select(m => kp2[m.TrainIdx].Pt).ToArray();
            return Cv2.FindHomography(InputArray.Create(srcPts), InputArray.Create(dstPts), HomographyMethods.Ransac, 3.0);
        }
    }
}
