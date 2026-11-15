#pragma once
#include <sps/plane.hpp>
#include <algorithm>
#include <cmath>
#include <stdexcept>

// Coordinates belong to the author's piecewise-affine plane, not to individual variables.
struct SearchWindow {
    Eigen::Vector2d lower = Eigen::Vector2d::Constant(-1.0);
    Eigen::Vector2d upper = Eigen::Vector2d::Constant(1.0);

    Eigen::Vector2d coordinate(int row, int column, int rows, int columns) const {
        Eigen::Vector2d result;
        const int indices[] = {row, column}, counts[] = {rows, columns};
        for (int axis = 0; axis < 2; ++axis)
            result(axis) = indices[axis] == 0 ? lower(axis) : indices[axis] == counts[axis] - 1 ? upper(axis)
                : lower(axis) + (upper(axis) - lower(axis)) * indices[axis] / (counts[axis] - 1);
        return result;
    }

    bool fits(const sps::AbstractPlane& plane, int rows, int columns) const {
        const auto valid = [&](const Eigen::Vector2d& point) {
            const auto values = plane.CalcParameters(point);
            return values.allFinite() && values.minCoeff() >= 0.0 && values.maxCoeff() <= 1.0;
        };
        // These are the vertices of the rectangle split by the plane's two diagonal seams.
        // Affine extrema on each piece occur at a vertex, so this certifies the whole window.
        for (double x : {lower(0), upper(0)}) {
            for (double y : {lower(1), upper(1)}) if (!valid({x, y})) return false;
            for (double y : {x, -x}) if (y >= lower(1) && y <= upper(1) && !valid({x, y})) return false;
        }
        for (double y : {lower(1), upper(1)})
            for (double x : {y, -y}) if (x >= lower(0) && x <= upper(0) && !valid({x, y})) return false;
        if (lower.maxCoeff() <= 0.0 && upper.minCoeff() >= 0.0 && !valid(Eigen::Vector2d::Zero())) return false;
        // Also check the exact floating-point evaluation used by displayed/selected cells.
        for (int row = 0; row < rows; ++row)
            for (int column = 0; column < columns; ++column)
                if (!valid(coordinate(row, column, rows, columns))) return false;
        return true;
    }

    static SearchWindow initial(const sps::AbstractPlane& plane, int rows, int columns) {
        double halfWidth = 1.0;
        for (int attempt = 0; attempt < 24; ++attempt) {
            // Normally the canonical square fits unchanged. A plane constructed at a variable
            // boundary may instead need a one-sided window retaining its selected origin.
            for (double x : {0.0, -halfWidth, halfWidth})
                for (double y : {0.0, -halfWidth, halfWidth}) {
                    SearchWindow window;
                    window.lower = Eigen::Vector2d(x - halfWidth, y - halfWidth);
                    window.upper = Eigen::Vector2d(x + halfWidth, y + halfWidth);
                    if (window.fits(plane, rows, columns)) return window;
                }
            halfWidth *= 0.5;
        }
        throw std::runtime_error("The SPS plane has no usable in-range sampling window. Resume an earlier grid and choose a different candidate.");
    }

    SearchWindow zoom(const sps::AbstractPlane& plane, const Eigen::Vector2d& selected, int rows, int columns) const {
        auto width = (0.5 * (upper - lower)).eval();
        for (int attempt = 0; attempt < 24; ++attempt) {
            SearchWindow child;
            child.lower = selected - 0.5 * width;
            child.upper = selected + 0.5 * width;
            if (child.fits(plane, rows, columns)) return child;
            // Translate the complete child into its certified parent. It retains the selected
            // point and its width, including when the parent legitimately extrapolates.
            for (int axis = 0; axis < 2; ++axis) {
                if (child.lower(axis) < lower(axis)) {
                    child.lower(axis) = lower(axis); child.upper(axis) = lower(axis) + width(axis);
                }
                if (child.upper(axis) > upper(axis)) {
                    child.upper(axis) = upper(axis); child.lower(axis) = upper(axis) - width(axis);
                }
            }
            if (child.fits(plane, rows, columns)) return child;
            // Defensive numerical fallback only; an exact half-width child always fits its parent.
            width *= 0.5;
        }
        throw std::runtime_error("The SPS plane could not retain the selected point in a valid sampling window.");
    }
};
