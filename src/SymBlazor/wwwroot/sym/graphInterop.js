window.symGraph = {
    render: function (element, figure) {
        if (!window.Plotly || !element) {
            return;
        }

        const data = figure?.data ?? [];
        const layout = figure?.layout ?? {};
        const config = figure?.config ?? {};
        window.Plotly.react(element, data, layout, config);
    },

    purge: function (element) {
        if (!window.Plotly || !element) {
            return;
        }

        window.Plotly.purge(element);
    }
};
