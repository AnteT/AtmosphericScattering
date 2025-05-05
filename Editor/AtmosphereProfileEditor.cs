using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System;

// Place this script in an 'Editor' folder within your package
[CustomEditor(typeof(AtmosphereProfile))]
public class AtmosphereProfileEditor : Editor
{
    // --- Layout Constants ---
    private const float GRAPH_HEIGHT = 150f;
    private const int GRAPH_SAMPLES = 100;
    private const float GRAPH_LEFT_PADDING = 35f;
    private const float GRAPH_RIGHT_PADDING = 10f;
    private const float GRAPH_TOP_PADDING = 5f;
    private const float GRAPH_BOTTOM_PADDING = 20f;
    private const float SPACE_BETWEEN_GRAPHS = 10f;

    // --- Drawing Constants ---
    private const float EPSILON = 1e-6f;
    private const float FILL_ALPHA = 0.3f;
    private const float LINE_THICKNESS = 2.0f;

    private static readonly Color LEGEND_RED = new Color(0.8745098f, 0.27450982f, 0.38039216f);
    private static readonly Color LEGEND_GREEN = new Color(0.40784314f, 0.7176471f, 0.43529412f);
    private static readonly Color LEGEND_BLUE = new Color(0.3137255f, 0.5882353f, 0.7490196f);

    private AtmosphereProfile _profile;
    private AtmosphereProfile.AtmosphereSettings _settings;

    // Graph Data
    private List<Vector2> _rayleighDensityPoints = new List<Vector2>(GRAPH_SAMPLES + 1);
    private List<Vector2> _mieDensityPoints = new List<Vector2>(GRAPH_SAMPLES + 1);
    private List<Vector2> _ozoneDensityPoints = new List<Vector2>(GRAPH_SAMPLES + 1);
    private List<Vector2> _finalRPoints = new List<Vector2>(GRAPH_SAMPLES + 1);
    private List<Vector2> _finalGPoints = new List<Vector2>(GRAPH_SAMPLES + 1);
    private List<Vector2> _finalBPoints = new List<Vector2>(GRAPH_SAMPLES + 1);
    private float _maxRgbExtinction = EPSILON;

    private void OnEnable()
    {
        _profile = (AtmosphereProfile)target;
        if (_profile != null && _profile.settings != null)
        {
            // Get direct settings reference AFTER ensuring internal factors are calculated
            // OnValidate should handle this, but OnEnable is a good backup/initial state setter
            _profile.settings.UpdateInternalScaleFactors(); // Make sure internal factors are correct
            _settings = _profile.settings;
            RecalculateGraphData(); // Calculate graph data based on potentially updated factors
        }
    }

    public override void OnInspectorGUI()
    {
        if (_profile == null || _settings == null)
        {
            DrawDefaultInspector();
            return;
        }
        if (_profile.settings == null) {
            _profile.settings = new AtmosphereProfile.AtmosphereSettings();
            EditorUtility.SetDirty(_profile); // Mark dirty if we created settings
            Debug.LogWarning($"Created missing settings for {_profile.name}");
        }        
        _settings = _profile.settings; // Update direct reference

        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        DrawDefaultInspector();
        bool settingsChanged = EditorGUI.EndChangeCheck();
        if (settingsChanged)
        {
            serializedObject.ApplyModifiedProperties();
        }

        // --- Visualization Section ---
        EditorGUILayout.Space(15);
        EditorGUILayout.LabelField("Atmosphere Profile Visualization", EditorStyles.boldLabel);
        EditorGUILayout.Space(5);

        // --- Graph 1: Densities ---
        EditorGUILayout.LabelField("Normalized Density Profiles vs Altitude", EditorStyles.miniBoldLabel);
        Rect densityAreaRect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth, GRAPH_HEIGHT + GRAPH_TOP_PADDING + GRAPH_BOTTOM_PADDING);
        Rect densityGraphRect = new Rect(
            densityAreaRect.x + GRAPH_LEFT_PADDING,
            densityAreaRect.y + GRAPH_TOP_PADDING,
            densityAreaRect.width - GRAPH_LEFT_PADDING - GRAPH_RIGHT_PADDING,
            GRAPH_HEIGHT
        );

        // --- Graph 2: Combined Scaled RGB Extinction ---
        EditorGUILayout.Space(SPACE_BETWEEN_GRAPHS);
        EditorGUILayout.LabelField("Combined Scaled RGB Extinction vs Altitude", EditorStyles.miniBoldLabel);
        Rect extinctionAreaRect = GUILayoutUtility.GetRect(EditorGUIUtility.currentViewWidth, GRAPH_HEIGHT + GRAPH_TOP_PADDING + GRAPH_BOTTOM_PADDING);
        Rect extinctionGraphRect = new Rect(
            extinctionAreaRect.x + GRAPH_LEFT_PADDING,
            extinctionAreaRect.y + GRAPH_TOP_PADDING,
            extinctionAreaRect.width - GRAPH_LEFT_PADDING - GRAPH_RIGHT_PADDING,
            GRAPH_HEIGHT
        );


        // Recalculate and redraw if needed
        if (settingsChanged || Event.current.type == EventType.Repaint)
        {
            RecalculateGraphData();

            DrawGraphBackground(densityGraphRect);
            DrawDensityGraphElements(densityGraphRect);
            DrawDensityGraphLabels(densityGraphRect, _settings.densityScale < 100f ? _settings.densityScale.ToString("F1") : _settings.densityScale.ToString("N0"));

            DrawGraphBackground(extinctionGraphRect);
            DrawRgbExtinctionGraphElements(extinctionGraphRect);
            DrawRgbExtinctionGraphLabels(extinctionGraphRect);
        }

        if (settingsChanged) Repaint();

        EditorGUILayout.Space(10);
    }

    // --- Graph Drawing Logic ---

    void DrawGraphBackground(Rect rect)
    {
        GUI.color = new Color(0.15f, 0.15f, 0.15f, 1f);
        GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
        GUI.color = Color.white;

        Handles.color = Color.gray * 0.8f;
        Handles.DrawLine(new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMin, rect.yMax)); // Y-Axis
        Handles.DrawLine(new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMax, rect.yMax)); // X-Axis
    }

    void RecalculateGraphData()
    {
        if (_settings == null) return;

        _rayleighDensityPoints.Clear();
        _mieDensityPoints.Clear();
        _ozoneDensityPoints.Clear();
        _finalRPoints.Clear();
        _finalGPoints.Clear();
        _finalBPoints.Clear();

        float atmoHeight = Mathf.Max(EPSILON, _settings.atmosphereHeight);
        float rayleighH = Mathf.Max(EPSILON, _settings.rayleighScaleHeightNorm * atmoHeight);
        float mieH = Mathf.Max(EPSILON, _settings.mieScaleHeightNorm * atmoHeight);

        Vector3 betaR = new Vector3(_settings.rayleighScatteringCoeff.r, _settings.rayleighScatteringCoeff.g, _settings.rayleighScatteringCoeff.b) * _settings.rayleighScaleFactorInternal;
        Vector3 betaM = new Vector3(_settings.mieScatteringCoeff.r, _settings.mieScatteringCoeff.g, _settings.mieScatteringCoeff.b) * _settings.mieScaleFactorInternal;
        Vector3 betaO = new Vector3(_settings.ozoneAbsorptionCoeff.r, _settings.ozoneAbsorptionCoeff.g, _settings.ozoneAbsorptionCoeff.b) * _settings.ozoneScaleFactorInternal;

        float maxDensityValue = 1.0f;
        _maxRgbExtinction = EPSILON;

        List<Vector2> tempRayleighDensity = new List<Vector2>(GRAPH_SAMPLES + 1);
        List<Vector2> tempMieDensity = new List<Vector2>(GRAPH_SAMPLES + 1);
        List<Vector2> tempOzoneDensity = new List<Vector2>(GRAPH_SAMPLES + 1);
        List<Vector2> tempRExt = new List<Vector2>(GRAPH_SAMPLES + 1);
        List<Vector2> tempGExt = new List<Vector2>(GRAPH_SAMPLES + 1);
        List<Vector2> tempBExt = new List<Vector2>(GRAPH_SAMPLES + 1);

        for (int i = 0; i <= GRAPH_SAMPLES; i++)
        {
            float normAltitude = (float)i / GRAPH_SAMPLES;
            float altitude = normAltitude * atmoHeight;

            float densityR = GetDensity(altitude, normAltitude, rayleighH, _settings.densityEdgeSmoothness);
            float densityM = GetDensity(altitude, normAltitude, mieH, _settings.densityEdgeSmoothness);
            float densityOzone = GetOzoneDensity(normAltitude, _settings.ozoneCenterAltitudeNorm, _settings.ozoneWidth);

            tempRayleighDensity.Add(new Vector2(normAltitude, Mathf.Clamp01(densityR / maxDensityValue)));
            tempMieDensity.Add(new Vector2(normAltitude, Mathf.Clamp01(densityM / maxDensityValue)));
            tempOzoneDensity.Add(new Vector2(normAltitude, Mathf.Clamp01(densityOzone)));

            Vector3 currentRayleighStr = betaR * densityR;
            Vector3 currentMieStr = betaM * densityM;
            Vector3 currentOzoneStr = betaO * densityOzone;

            float rExt = (currentRayleighStr.x + currentMieStr.x + currentOzoneStr.x) * _settings.densityScale;
            float gExt = (currentRayleighStr.y + currentMieStr.y + currentOzoneStr.y) * _settings.densityScale;
            float bExt = (currentRayleighStr.z + currentMieStr.z + currentOzoneStr.z) * _settings.densityScale;

            tempRExt.Add(new Vector2(normAltitude, rExt));
            tempGExt.Add(new Vector2(normAltitude, gExt));
            tempBExt.Add(new Vector2(normAltitude, bExt));

            _maxRgbExtinction = Mathf.Max(_maxRgbExtinction, rExt, gExt, bExt);
        }

        _maxRgbExtinction = Mathf.Max(_maxRgbExtinction, EPSILON);

        _rayleighDensityPoints = new List<Vector2>(tempRayleighDensity);
        _mieDensityPoints = new List<Vector2>(tempMieDensity);
        _ozoneDensityPoints = new List<Vector2>(tempOzoneDensity);
        _finalRPoints = new List<Vector2>(tempRExt);
        _finalGPoints = new List<Vector2>(tempGExt);
        _finalBPoints = new List<Vector2>(tempBExt);
    }


    // --- Combined Drawing Functions ---

    void DrawDensityGraphElements(Rect rect)
    {
        if (_settings == null) return;

        Color rayleighLineColor = NormalizeColor(_settings.rayleighScatteringCoeff); rayleighLineColor.a = 1f;
        Color mieLineColor = NormalizeColor(_settings.mieScatteringCoeff); mieLineColor.a = 1f;
        Color ozoneLineColor = NormalizeColor(_settings.ozoneAbsorptionCoeff); ozoneLineColor.a = 1f;

        List<Vector2> mappedRayleigh = MapPointsToGraph(_rayleighDensityPoints, rect, 1.0f);
        List<Vector2> mappedMie = MapPointsToGraph(_mieDensityPoints, rect, 1.0f);
        List<Vector2> mappedOzone = MapPointsToGraph(_ozoneDensityPoints, rect, 1.0f);

        Vector3[] rayleighPoints3D = ConvertToVector3Array(mappedRayleigh);
        Vector3[] miePoints3D = ConvertToVector3Array(mappedMie);
        Vector3[] ozonePoints3D = ConvertToVector3Array(mappedOzone);

        DrawGraphFillQuads(_settings.mieScatteringCoeff, miePoints3D, rect);
        DrawGraphFillQuads(_settings.ozoneAbsorptionCoeff, ozonePoints3D, rect);
        DrawGraphFillQuads(_settings.rayleighScatteringCoeff, rayleighPoints3D, rect);

        Handles.color = mieLineColor; if (miePoints3D.Length > 1) Handles.DrawAAPolyLine(LINE_THICKNESS, miePoints3D);
        Handles.color = ozoneLineColor; if (ozonePoints3D.Length > 1) Handles.DrawAAPolyLine(LINE_THICKNESS, ozonePoints3D);
        Handles.color = rayleighLineColor; if (rayleighPoints3D.Length > 1) Handles.DrawAAPolyLine(LINE_THICKNESS, rayleighPoints3D);

        Handles.color = Color.white;
    }

    void DrawRgbExtinctionGraphElements(Rect rect)
    {
        if (_settings == null) return;

        List<Vector2> mappedR = MapPointsToGraph(_finalRPoints, rect, _maxRgbExtinction);
        List<Vector2> mappedG = MapPointsToGraph(_finalGPoints, rect, _maxRgbExtinction);
        List<Vector2> mappedB = MapPointsToGraph(_finalBPoints, rect, _maxRgbExtinction);

        Vector3[] rPoints3D = ConvertToVector3Array(mappedR);
        Vector3[] gPoints3D = ConvertToVector3Array(mappedG);
        Vector3[] bPoints3D = ConvertToVector3Array(mappedB);

        DrawGraphFillQuads(LEGEND_BLUE, bPoints3D, rect);
        DrawGraphFillQuads(LEGEND_GREEN, gPoints3D, rect);
        DrawGraphFillQuads(LEGEND_RED, rPoints3D, rect);

        Handles.color = LEGEND_BLUE; if (bPoints3D.Length > 1) Handles.DrawAAPolyLine(LINE_THICKNESS, bPoints3D);
        Handles.color = LEGEND_GREEN; if (gPoints3D.Length > 1) Handles.DrawAAPolyLine(LINE_THICKNESS, gPoints3D);
        Handles.color = LEGEND_RED; if (rPoints3D.Length > 1) Handles.DrawAAPolyLine(LINE_THICKNESS, rPoints3D);

        Handles.color = Color.white;
    }

    void DrawGraphFillQuads(Color fillCol, Vector3[] linePoints, Rect graphRect)
    {
        if (linePoints == null || linePoints.Length < 2) return;

        Color transparentColor = fillCol;
        float maxVal = Mathf.Max(Mathf.Max(transparentColor.r, transparentColor.g), transparentColor.b);
        transparentColor.r = fillCol.r / maxVal;
        transparentColor.g = fillCol.g / maxVal;
        transparentColor.b = fillCol.b / maxVal;
        transparentColor.a = FILL_ALPHA;
        Handles.color = transparentColor;

        Vector3[] quadVerts = new Vector3[4];
        for (int i = 0; i < linePoints.Length - 1; i++)
        {
            quadVerts[0] = linePoints[i];
            quadVerts[1] = linePoints[i+1];
            quadVerts[2] = new Vector3(linePoints[i+1].x, graphRect.yMax, 0f);
            quadVerts[3] = new Vector3(linePoints[i].x,   graphRect.yMax, 0f);
            try { Handles.DrawAAConvexPolygon(quadVerts); }
            catch (System.ArgumentException) { /* Ignore */ }
        }
        Handles.color = Color.white;
    }

    // --- Label Drawing Functions ---

    void DrawDensityGraphLabels(Rect graphRect, string yMaxLabel)
    {
        GUIStyle miniLabel = EditorStyles.miniLabel;
        GUIStyle miniLabelRightAlign = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        float labelWidth = 36f;
        float labelHeight = 15f;
        float yAxisLabelOffset = 2f;

        EditorGUI.LabelField(new Rect(graphRect.xMin - labelWidth - yAxisLabelOffset, graphRect.yMin - labelHeight * 0.5f, labelWidth, labelHeight), yMaxLabel, miniLabelRightAlign);
        EditorGUI.LabelField(new Rect(graphRect.xMin - labelWidth - yAxisLabelOffset, graphRect.yMax - labelHeight * 0.5f, labelWidth, labelHeight), "0", miniLabelRightAlign);
        Rect yTitleRect = new Rect(graphRect.xMin - labelWidth - yAxisLabelOffset - 10, graphRect.center.y - labelHeight * 0.5f, labelWidth + 5, labelHeight);
        EditorGUI.LabelField(yTitleRect, "Density", miniLabel);

        float xAxisLabelY = graphRect.yMax + 2;
        EditorGUI.LabelField(new Rect(graphRect.xMin, xAxisLabelY, 30, labelHeight), "Surf", miniLabel);
        EditorGUI.LabelField(new Rect(graphRect.xMax - 20, xAxisLabelY, 30, labelHeight), "Top", miniLabel);
        EditorGUI.LabelField(new Rect(graphRect.center.x - 20, xAxisLabelY, 40, labelHeight), "Altitude", miniLabel);

        float legendWidth = 70f;
        float legendHeight = 15f * 3 + 4;
        float legendX = graphRect.xMax - legendWidth - 5;
        float legendY = graphRect.yMin + 5;
        float spacing = 15f; float boxSize = 8f;
        EditorGUI.DrawRect(new Rect(legendX - 5, legendY - 2, legendWidth + 10, legendHeight), new Color(0.1f, 0.1f, 0.1f, 0.7f));
        Color rCol = NormalizeColor(_settings.rayleighScatteringCoeff); rCol.a=1f;
        Color mCol = NormalizeColor(_settings.mieScatteringCoeff); mCol.a=1f;
        Color oCol = NormalizeColor(_settings.ozoneAbsorptionCoeff); oCol.a=1f;
        EditorGUI.LabelField(new Rect(legendX + boxSize + 2, legendY, 50, spacing), "Rayleigh", miniLabel); EditorGUI.DrawRect(new Rect(legendX, legendY + 4, boxSize, boxSize), rCol); legendY += spacing;
        EditorGUI.LabelField(new Rect(legendX + boxSize + 2, legendY, 50, spacing), "Mie", miniLabel); EditorGUI.DrawRect(new Rect(legendX, legendY + 4, boxSize, boxSize), mCol); legendY += spacing;
        EditorGUI.LabelField(new Rect(legendX + boxSize + 2, legendY, 50, spacing), "Ozone", miniLabel); EditorGUI.DrawRect(new Rect(legendX, legendY + 4, boxSize, boxSize), oCol);
    }

    void DrawRgbExtinctionGraphLabels(Rect graphRect)
    {
        GUIStyle miniLabel = EditorStyles.miniLabel;
        GUIStyle miniLabelRightAlign = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        float labelWidth = 36f;
        float labelHeight = 15f;
        float yAxisLabelOffset = 2f;

        string maxLabel = _maxRgbExtinction.ToString(_maxRgbExtinction > 0.01f ? "F3" : "E1");
        EditorGUI.LabelField(new Rect(graphRect.xMin - labelWidth - yAxisLabelOffset, graphRect.yMin - labelHeight * 0.5f, labelWidth, labelHeight), maxLabel, miniLabelRightAlign);
        EditorGUI.LabelField(new Rect(graphRect.xMin - labelWidth - yAxisLabelOffset, graphRect.yMax - labelHeight * 0.5f, labelWidth, labelHeight), "0", miniLabelRightAlign);
        Rect yTitleRect = new Rect(graphRect.xMin - labelWidth - yAxisLabelOffset - 10, graphRect.center.y - labelHeight * 0.5f, labelWidth + 5, labelHeight);
        EditorGUI.LabelField(yTitleRect, "Extinct", miniLabel);

        float xAxisLabelY = graphRect.yMax + 2;
        EditorGUI.LabelField(new Rect(graphRect.xMin, xAxisLabelY, 30, labelHeight), "Surf", miniLabel);
        EditorGUI.LabelField(new Rect(graphRect.xMax - 20, xAxisLabelY, 30, labelHeight), "Top", miniLabel);
        EditorGUI.LabelField(new Rect(graphRect.center.x - 20, xAxisLabelY, 40, labelHeight), "Altitude", miniLabel);

        float legendTextWidth = 40f;
        float legendTotalWidth = legendTextWidth + 15f;
        float legendHeight = 13f * 3 + 6;
        float legendX = graphRect.xMax - legendTotalWidth - 5;
        float legendY = graphRect.yMin + 5;
        float spacing = 13f;
        float boxSize = 6f;

        EditorGUI.DrawRect(new Rect(legendX - 5, legendY - 3, legendTotalWidth + 10, legendHeight), new Color(0.1f, 0.1f, 0.1f, 0.7f));
        EditorGUI.DrawRect(new Rect(legendX, legendY + 3, boxSize, boxSize), LEGEND_RED); EditorGUI.LabelField(new Rect(legendX + boxSize + 2, legendY, legendTextWidth, spacing), "Red", miniLabel); legendY += spacing;
        EditorGUI.DrawRect(new Rect(legendX, legendY + 3, boxSize, boxSize), LEGEND_GREEN); EditorGUI.LabelField(new Rect(legendX + boxSize + 2, legendY, legendTextWidth, spacing), "Green", miniLabel); legendY += spacing;
        EditorGUI.DrawRect(new Rect(legendX, legendY + 3, boxSize, boxSize), LEGEND_BLUE); EditorGUI.LabelField(new Rect(legendX + boxSize + 2, legendY, legendTextWidth, spacing), "Blue", miniLabel);
    }


    // --- Utility and Helper Functions ---
    Vector2 MapToGraph(float normX, float normY, Rect graphRect)
    {
        float x = Mathf.Lerp(graphRect.xMin, graphRect.xMax, normX);
        float y = Mathf.Lerp(graphRect.yMax, graphRect.yMin, normY); // Y is inverted (0 is bottom)
        return new Vector2(x, y);
    }

    List<Vector2> MapPointsToGraph(List<Vector2> rawPoints, Rect graphRect, float maxValue)
    {
        List<Vector2> mappedPoints = new List<Vector2>(rawPoints.Count);
        maxValue = Mathf.Max(maxValue, EPSILON);

        foreach (Vector2 point in rawPoints)
        {
            float normX = point.x;
            float normY = Mathf.Clamp01(point.y / maxValue);
            mappedPoints.Add(MapToGraph(normX, normY, graphRect));
        }
        return mappedPoints;
    }

    Vector3[] ConvertToVector3Array(List<Vector2> points2D)
    {
        if (points2D == null) return new Vector3[0];
        Vector3[] points3D = new Vector3[points2D.Count];
        for (int i = 0; i < points2D.Count; i++)
        {
            points3D[i] = new Vector3(points2D[i].x, points2D[i].y, 0f);
        }
        return points3D;
    }

    float GetDensity(float altitude, float normAltitude, float scaleHeight, float edgeSmoothness)
    {
        scaleHeight = Mathf.Max(EPSILON, scaleHeight);
        float expDensity = Mathf.Exp(-altitude / scaleHeight);
        float smoothedDensity = expDensity * Mathf.Clamp01(1.0f - normAltitude);
        return Mathf.Lerp(expDensity, smoothedDensity, edgeSmoothness);
    }

    float GetOzoneDensity(float normAltitude, float ozoneCenterNormAlt, float ozoneWidth)
    {
        float x = (normAltitude - ozoneCenterNormAlt) / Mathf.Max(EPSILON, ozoneWidth * 0.5f);
        return Mathf.Exp(-x * x);
    }

    Color NormalizeColor(Color color)
    {
        float maxVal = Mathf.Max(Mathf.Max(color.r, color.g), color.b);
        color.r /= maxVal;
        color.g /= maxVal;
        color.b /= maxVal;
        return color;
    }
}