function getImageNaturalSize(image) {
    return {
        width: image.naturalWidth,
        height: image.naturalHeight
    };
}

var annotator = {
    init: function (canvas, image, dotNetHelper) {
        this.canvas = canvas;
        this.image = image;
        this.dotNetHelper = dotNetHelper;
        this.ctx = canvas.getContext('2d');
        this.isDrawing = false;
        this.startX = 0;
        this.startY = 0;
        this.boxes = [];

        // Resize canvas to match image
        this.resizeCanvas();

        // Event listeners
        this.canvas.addEventListener('mousedown', this.startDrawing.bind(this));
        this.canvas.addEventListener('mousemove', this.draw.bind(this));
        this.canvas.addEventListener('mouseup', this.stopDrawing.bind(this));
        this.canvas.addEventListener('mouseleave', this.stopDrawing.bind(this));

        // Touch events
        this.canvas.addEventListener('touchstart', this.startDrawing.bind(this));
        this.canvas.addEventListener('touchmove', this.draw.bind(this));
        this.canvas.addEventListener('touchend', this.stopDrawing.bind(this));

        window.addEventListener('resize', this.resizeCanvas.bind(this));
    },

    resizeCanvas: function () {
        this.canvas.width = this.image.clientWidth;
        this.canvas.height = this.image.clientHeight;
        this.redraw();
    },

    startDrawing: function (e) {
        if (e.cancelable) e.preventDefault();
        this.isDrawing = true;
        const pos = this.getCanvasCoordinates(e);
        this.startX = pos.x;
        this.startY = pos.y;
    },

    draw: function (e) {
        if (!this.isDrawing) return;
        if (e.cancelable) e.preventDefault();
        const pos = this.getCanvasCoordinates(e);
        this.redraw(); // Redraw existing boxes
        this.ctx.strokeStyle = 'red';
        this.ctx.lineWidth = 2;
        this.ctx.strokeRect(this.startX, this.startY, pos.x - this.startX, pos.y - this.startY);
    },

    stopDrawing: function (e) {
        if (!this.isDrawing) return;
        if (e.cancelable) e.preventDefault();
        this.isDrawing = false;
        const pos = this.getCanvasCoordinates(e);
        const box = {
            startX: Math.min(this.startX, pos.x),
            startY: Math.min(this.startY, pos.y),
            endX: Math.max(this.startX, pos.x),
            endY: Math.max(this.startY, pos.y)
        };

        // Ensure box has a size
        if (box.endX - box.startX > 5 && box.endY - box.startY > 5) {
            this.dotNetHelper.invokeMethodAsync('AddBox',
                box.startX / this.canvas.width,
                box.startY / this.canvas.height,
                box.endX / this.canvas.width,
                box.endY / this.canvas.height);
        }
    },

    addBox: function (box) {
        // box here has normalized coordinates from .NET (0 to 1)
        this.boxes.push(box);
        this.redraw();
    },

    removeLastBox: function () {
        this.boxes.pop();
        this.redraw();
    },

    getCanvasCoordinates: function (e) {
        const rect = this.canvas.getBoundingClientRect();
        let clientX, clientY;

        if (e.touches && e.touches.length > 0) {
            clientX = e.touches[0].clientX;
            clientY = e.touches[0].clientY;
        } else if (e.changedTouches && e.changedTouches.length > 0) {
            clientX = e.changedTouches[0].clientX;
            clientY = e.changedTouches[0].clientY;
        } else {
            clientX = e.clientX;
            clientY = e.clientY;
        }

        return {
            x: (clientX - rect.left) * (this.canvas.width / rect.width),
            y: (clientY - rect.top) * (this.canvas.height / rect.height)
        };
    },

    redraw: function () {
        this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        this.boxes.forEach(box => {
            const sx = box.startX * this.canvas.width;
            const sy = box.startY * this.canvas.height;
            const ex = box.endX * this.canvas.width;
            const ey = box.endY * this.canvas.height;

            this.ctx.strokeStyle = box.color || 'lime';
            this.ctx.lineWidth = 2;
            this.ctx.strokeRect(sx, sy, ex - sx, ey - sy);

            this.ctx.fillStyle = box.color || 'lime';
            this.ctx.font = 'bold 16px sans-serif';
            const labelY = sy > 20 ? sy - 5 : sy + 20;
            this.ctx.fillText(box.label, sx, labelY);
        });
    },

    clear: function () {
        this.boxes = [];
        this.redraw();
    },

    destroy: function () {
        // Clean up
        this.canvas.replaceWith(this.canvas.cloneNode(true));
    }
};