// Light/dark theme. Loaded in <head> so the saved choice is applied before first paint (no flash).
(function () {
    var KEY = "household.theme";

    function preferred() {
        var saved = localStorage.getItem(KEY);
        if (saved === "light" || saved === "dark") return saved;
        return window.matchMedia && window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }

    function apply(theme) {
        document.documentElement.setAttribute("data-bs-theme", theme);
    }

    apply(preferred());

    window.appTheme = {
        get: function () { return document.documentElement.getAttribute("data-bs-theme"); },
        set: function (theme) {
            localStorage.setItem(KEY, theme);
            apply(theme);
        }
    };
})();
